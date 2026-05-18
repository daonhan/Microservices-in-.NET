using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Models;
using Saga.Service.Observability;
using Saga.Service.StateMachines;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed partial class RefundSagaReplyProcessor
{
    private readonly SagaContext _sagaContext;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefundSagaReplyProcessor> _logger;

    public RefundSagaReplyProcessor(
        SagaContext sagaContext,
        IOutboxUnitOfWork outboxUnitOfWork,
        TimeProvider timeProvider,
        ILogger<RefundSagaReplyProcessor> logger)
    {
        _sagaContext = sagaContext;
        _outboxUnitOfWork = outboxUnitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(Event @event)
    {
        if (@event.SagaId is not { } sagaId)
        {
            return;
        }

        await _outboxUnitOfWork.ExecuteAsync(_sagaContext.Database.CreateExecutionStrategy(), async () =>
        {
            var saga = await _sagaContext.SagaInstances
                .Include(s => s.RefundSagaState)
                .FirstOrDefaultAsync(s => s.SagaId == sagaId);
            if (saga?.RefundSagaState is null)
            {
                return [];
            }

            var currentStep = Enum.Parse<RefundSagaStep>(saga.CurrentStep);
            var snapshot = new RefundSagaStateSnapshot(
                saga.SagaId,
                saga.RefundSagaState.OrderId,
                saga.RefundSagaState.PaymentId,
                saga.RefundSagaState.ShipmentId,
                saga.RefundSagaState.RefundAmount,
                saga.RefundSagaState.Currency,
                currentStep,
                saga.Status,
                saga.RefundSagaState.LastStepResult);
            var result = RefundSagaStateMachine.Transition(snapshot, @event);
            if (!result.Changed)
            {
                return [];
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var previousStatus = saga.Status;
            var stepSeconds = Math.Max(0d, (now - saga.UpdatedAt).TotalSeconds);
            using var activity = SagaTelemetry.StartTransition(
                saga.SagaId,
                saga.SagaType,
                currentStep.ToString(),
                result.State.CurrentStep.ToString());

            saga.CurrentStep = result.State.CurrentStep.ToString();
            saga.Status = result.State.Status;
            saga.UpdatedAt = now;
            saga.RefundSagaState.LastStepResult = result.State.LastStepResult;
            saga.LastCommandId = result.Commands.Count == 0 ? null : result.Commands[0].Id;
            saga.Transitions.Add(new SagaTransition
            {
                SagaId = saga.SagaId,
                FromStep = currentStep.ToString(),
                ToStep = result.State.CurrentStep.ToString(),
                Timestamp = now,
                TriggerMessageId = @event.Id,
                TriggerKind = SagaTriggerKind.Event,
                Error = ExtractError(@event)
            });

            await _sagaContext.SaveChangesAsync();

            RecordTransitionTelemetry(saga, previousStatus, currentStep, stepSeconds, @event);

            return result.Commands;
        });
    }

    private void RecordTransitionTelemetry(
        SagaInstance saga,
        SagaStatus previousStatus,
        RefundSagaStep fromStep,
        double stepSeconds,
        Event @event)
    {
        var type = new KeyValuePair<string, object?>("type", saga.SagaType);

        SagaTelemetry.StepDuration.Record(
            stepSeconds,
            type,
            new KeyValuePair<string, object?>("step", fromStep.ToString()));

        if (previousStatus == SagaStatus.Running && saga.Status == SagaStatus.Compensating)
        {
            SagaTelemetry.Compensation.Add(1, type);
        }

        if (saga.Status == SagaStatus.Completed || saga.Status == SagaStatus.Compensated)
        {
            SagaTelemetry.Completed.Add(1, type);
        }
        else if (saga.Status == SagaStatus.Failed)
        {
            SagaTelemetry.Failed.Add(
                1,
                type,
                new KeyValuePair<string, object?>(
                    "reason",
                    ExtractError(@event) ?? saga.RefundSagaState?.LastStepResult ?? "Unknown"));
        }

        LogRefundSagaTransition(
            _logger,
            saga.SagaId,
            saga.CurrentStep,
            @event.Id,
            @event.CausationId);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Refund saga {SagaId} transitioned to step {Step} (MessageId {MessageId}, CausationId {CausationId})")]
    private static partial void LogRefundSagaTransition(
        ILogger logger,
        Guid sagaId,
        string step,
        Guid messageId,
        Guid? causationId);

    private static string? ExtractError(Event @event) => @event switch
    {
        PaymentFailedEvent failed => $"Refund failed: {failed.Reason}",
        ShipmentFailedEvent failed => $"Shipment action failed: {failed.Reason}",
        _ => null
    };
}
