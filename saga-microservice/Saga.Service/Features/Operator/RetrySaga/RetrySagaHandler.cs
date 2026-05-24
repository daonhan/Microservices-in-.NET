using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Saga.Service.Domain;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Reaper;

namespace Saga.Service.Features.Operator.RetrySaga;

internal sealed class RetrySagaHandler
{
    private const string SagaPathPrefix = "/operator/api/sagas";

    private readonly SagaContext _sagaContext;
    private readonly IOutboxStore _outboxStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly OrderSagaTimeoutScheduler _timeoutScheduler;
    private readonly TimeProvider _timeProvider;

    public RetrySagaHandler(
        SagaContext sagaContext,
        IOutboxStore outboxStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        OrderSagaTimeoutScheduler timeoutScheduler,
        TimeProvider timeProvider)
    {
        _sagaContext = sagaContext;
        _outboxStore = outboxStore;
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
                .FirstOrDefaultAsync(s => s.SagaId == id, cancellationToken);

            if (saga is null)
            {
                return [];
            }

            if (saga.Status is not (SagaStatus.Running or SagaStatus.Compensating))
            {
                result = Results.Conflict(new { id, reason = $"status_{saga.Status}" });
                return [];
            }

            if (saga.LastCommandId is not { } commandId)
            {
                result = Results.Conflict(new { id, reason = "no_in_flight_command" });
                return [];
            }

            if (!await _outboxStore.RequeueOutboxEvent(commandId))
            {
                result = Results.Conflict(new { id, reason = "in_flight_command_not_found" });
                return [];
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            saga.RetryCount = 0;
            saga.UpdatedAt = now;
            _timeoutScheduler.MoveForward(saga, now);
            saga.Transitions.Add(new SagaTransition
            {
                SagaId = saga.SagaId,
                FromStep = saga.CurrentStep,
                ToStep = saga.CurrentStep,
                Timestamp = now,
                TriggerMessageId = commandId,
                TriggerKind = SagaTriggerKind.OperatorAction,
                Error = "Operator retry requeued the in-flight command."
            });

            await _sagaContext.SaveChangesAsync(cancellationToken);

            result = Results.Accepted(
                $"{SagaPathPrefix}/{saga.SagaId}",
                new RetrySagaResponse(
                    saga.SagaId,
                    saga.Status.ToString(),
                    saga.CurrentStep,
                    saga.LastCommandId));
            return [];
        });

        return result;
    }
}
