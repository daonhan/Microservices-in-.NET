using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Observability;
using Saga.Service.Infrastructure.Reaper;

namespace Saga.Service.Features.Operator.AbortSaga;

internal sealed class AbortSagaHandler
{
    private const string SagaPathPrefix = "/operator/api/sagas";

    private readonly SagaContext _sagaContext;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly OrderSagaTimeoutScheduler _timeoutScheduler;
    private readonly TimeProvider _timeProvider;

    public AbortSagaHandler(
        SagaContext sagaContext,
        IOutboxUnitOfWork outboxUnitOfWork,
        OrderSagaTimeoutScheduler timeoutScheduler,
        TimeProvider timeProvider)
    {
        _sagaContext = sagaContext;
        _outboxUnitOfWork = outboxUnitOfWork;
        _timeoutScheduler = timeoutScheduler;
        _timeProvider = timeProvider;
    }

    public async Task<IResult> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        IResult result = TypedResults.NotFound();

        await _outboxUnitOfWork.ExecuteAsync(_sagaContext.Database.CreateExecutionStrategy(), async () =>
        {
            var saga = await _sagaContext.SagaInstances
                .Include(s => s.OrderSagaState)
                .FirstOrDefaultAsync(s => s.SagaId == id, cancellationToken);

            if (saga is null)
            {
                return [];
            }

            if (saga.OrderSagaState is null)
            {
                result = Results.Conflict(new { id, reason = "unsupported_saga_type" });
                return [];
            }

            if (saga.Status != SagaStatus.Running)
            {
                result = Results.Conflict(new { id, reason = $"status_{saga.Status}" });
                return [];
            }

            if (!Enum.TryParse<OrderSagaStep>(saga.CurrentStep, out var currentStep))
            {
                result = Results.Conflict(new { id, reason = "unknown_current_step" });
                return [];
            }

            var trigger = new Event
            {
                CorrelationId = saga.CorrelationId,
                SagaId = saga.SagaId
            };
            var snapshot = new OrderSagaStateSnapshot(
                saga.SagaId,
                saga.OrderSagaState.OrderId,
                currentStep,
                saga.Status,
                saga.OrderSagaState.LastStepResult,
                saga.OrderSagaState.Amount,
                ParseStep(saga.OrderSagaState.CompensationOrigin));
            var origin = OrderSagaStateMachine.GetLastCompletedStep(currentStep);
            var transition = OrderSagaStateMachine.BeginCompensation(snapshot, origin, trigger);

            if (!transition.Changed)
            {
                result = Results.Conflict(new { id, reason = "compensation_not_started" });
                return [];
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var previousStatus = saga.Status;
            saga.CurrentStep = transition.State.CurrentStep.ToString();
            saga.Status = transition.State.Status;
            saga.UpdatedAt = now;
            saga.OrderSagaState.LastStepResult = transition.State.LastStepResult;
            saga.OrderSagaState.Amount = transition.State.Amount;
            saga.OrderSagaState.CompensationOrigin = transition.State.CompensationOrigin?.ToString();
            saga.LastCommandId = transition.Commands.Count == 0 ? null : transition.Commands[0].Id;
            _timeoutScheduler.Apply(saga, now);
            saga.Transitions.Add(new SagaTransition
            {
                SagaId = saga.SagaId,
                FromStep = currentStep.ToString(),
                ToStep = transition.State.CurrentStep.ToString(),
                Timestamp = now,
                TriggerMessageId = trigger.Id,
                TriggerKind = SagaTriggerKind.OperatorAction,
                Error = "Operator abort started saga compensation."
            });

            await _sagaContext.SaveChangesAsync(cancellationToken);

            if (previousStatus == SagaStatus.Running && saga.Status == SagaStatus.Compensating)
            {
                SagaTelemetry.Compensation.Add(1, new KeyValuePair<string, object?>("type", saga.SagaType));
            }

            result = Results.Accepted(
                $"{SagaPathPrefix}/{saga.SagaId}",
                new AbortSagaResponse(
                    saga.SagaId,
                    saga.Status.ToString(),
                    saga.CurrentStep,
                    saga.LastCommandId));
            return transition.Commands;
        });

        return result;
    }

    private static OrderSagaStep? ParseStep(string? value) =>
        Enum.TryParse<OrderSagaStep>(value, out var parsed) ? parsed : null;
}
