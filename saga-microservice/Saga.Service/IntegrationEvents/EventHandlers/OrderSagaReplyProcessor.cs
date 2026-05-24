using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Infrastructure.Observability;
using Saga.Service.Infrastructure.Reaper;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed partial class OrderSagaReplyProcessor
{
    private readonly ISagaInstanceStore _sagaStore;
    private readonly TimeProvider _timeProvider;
    private readonly OrderSagaTimeoutScheduler _timeoutScheduler;
    private readonly ILogger<OrderSagaReplyProcessor> _logger;

    public OrderSagaReplyProcessor(
        ISagaInstanceStore sagaStore,
        TimeProvider timeProvider,
        OrderSagaTimeoutScheduler timeoutScheduler,
        ILogger<OrderSagaReplyProcessor> logger)
    {
        _sagaStore = sagaStore;
        _timeProvider = timeProvider;
        _timeoutScheduler = timeoutScheduler;
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
            var saga = await _sagaStore.GetOrderSagaBySagaId(sagaId);
            if (saga?.OrderSagaState is null)
            {
                return [];
            }

            var currentStep = Enum.Parse<OrderSagaStep>(saga.CurrentStep);
            var compensationOrigin = ParseStep(saga.OrderSagaState.CompensationOrigin);
            var snapshot = new OrderSagaStateSnapshot(
                saga.SagaId,
                saga.OrderSagaState.OrderId,
                currentStep,
                saga.Status,
                saga.OrderSagaState.LastStepResult,
                saga.OrderSagaState.Amount,
                compensationOrigin);
            var result = OrderSagaStateMachine.Transition(snapshot, @event);
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
            saga.OrderSagaState.LastStepResult = result.State.LastStepResult;
            saga.OrderSagaState.Amount = result.State.Amount;
            saga.OrderSagaState.CompensationOrigin = result.State.CompensationOrigin?.ToString();
            saga.LastCommandId = result.Commands.Count == 0 ? null : result.Commands[0].Id;
            _timeoutScheduler.Apply(saga, now);
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
        OrderSagaStep fromStep,
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

        if (saga.Status == SagaStatus.Completed)
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
                    ExtractError(@event) ?? saga.OrderSagaState?.LastStepResult ?? "Unknown"));
        }

        LogSagaTransition(
            _logger,
            saga.SagaId,
            saga.SagaType,
            saga.CurrentStep,
            @event.Id,
            @event.CausationId);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Saga {SagaId} ({SagaType}) transitioned to step {Step} (MessageId {MessageId}, CausationId {CausationId})")]
    private static partial void LogSagaTransition(
        ILogger logger,
        Guid sagaId,
        string sagaType,
        string step,
        Guid messageId,
        Guid? causationId);

    private static OrderSagaStep? ParseStep(string? value) =>
        Enum.TryParse<OrderSagaStep>(value, out var parsed) ? parsed : null;

    private static string? ExtractError(Event @event) => @event switch
    {
        StockReservationFailedEvent => "Stock reservation failed.",
        PaymentFailedEvent failed => $"Payment failed: {failed.Reason}",
        ShipmentFailedEvent failed => $"Shipment failed: {failed.Reason}",
        _ => null
    };
}
