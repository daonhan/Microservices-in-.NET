using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.RefundSaga;
using Saga.Service.Observability;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed partial class RefundSagaReplyProcessor
{
    private readonly ISagaInstanceStore _sagaStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefundSagaReplyProcessor> _logger;

    public RefundSagaReplyProcessor(
        ISagaInstanceStore sagaStore,
        TimeProvider timeProvider,
        ILogger<RefundSagaReplyProcessor> logger)
    {
        _sagaStore = sagaStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(Event @event)
    {
        if (@event.SagaId is not { } sagaId)
        {
            return;
        }

        await _sagaStore.ExecuteAsync(async () =>
        {
            var saga = await _sagaStore.GetRefundSagaBySagaId(sagaId);
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

            await _sagaStore.SaveChangesAsync();

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
