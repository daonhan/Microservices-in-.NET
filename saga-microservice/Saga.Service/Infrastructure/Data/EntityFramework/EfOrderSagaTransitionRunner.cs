using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Infrastructure.Observability;
using Saga.Service.Infrastructure.Reaper;

namespace Saga.Service.Infrastructure.Data.EntityFramework;

internal sealed partial class EfOrderSagaTransitionRunner
    : ISagaTransitionRunner<OrderSagaStateSnapshot, Event>
{
    private readonly ISagaInstanceStore _sagaStore;
    private readonly TimeProvider _timeProvider;
    private readonly OrderSagaTimeoutScheduler _timeoutScheduler;
    private readonly ILogger<EfOrderSagaTransitionRunner> _logger;

    public EfOrderSagaTransitionRunner(
        ISagaInstanceStore sagaStore,
        TimeProvider timeProvider,
        OrderSagaTimeoutScheduler timeoutScheduler,
        ILogger<EfOrderSagaTransitionRunner> logger)
    {
        _sagaStore = sagaStore;
        _timeProvider = timeProvider;
        _timeoutScheduler = timeoutScheduler;
        _logger = logger;
    }

    public async Task RunAsync(
        Guid sagaId,
        Event trigger,
        Func<OrderSagaStateSnapshot, Event, TransitionResult<OrderSagaStateSnapshot>> transitionFn,
        CancellationToken cancellationToken = default)
    {
        await _sagaStore.ExecuteAsync(async () =>
        {
            var saga = await _sagaStore.GetOrderSagaBySagaId(sagaId, cancellationToken);
            if (saga?.OrderSagaState is null)
            {
                return [];
            }

            var currentStep = Enum.Parse<OrderSagaStep>(saga.CurrentStep);
            var snapshot = SnapshotFromSaga(saga, currentStep);
            var result = transitionFn(snapshot, trigger);
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

            ApplyTransitionToSaga(saga, result, now);
            _timeoutScheduler.Apply(saga, now);
            saga.Transitions.Add(new SagaTransition
            {
                SagaId = saga.SagaId,
                FromStep = currentStep.ToString(),
                ToStep = result.State.CurrentStep.ToString(),
                Timestamp = now,
                TriggerMessageId = trigger.Id,
                TriggerKind = SagaTriggerKind.Event,
                Error = ExtractError(trigger)
            });

            await _sagaStore.SaveChangesAsync(cancellationToken);

            RecordTransitionTelemetry(saga, previousStatus, currentStep, stepSeconds, trigger);

            return result.Commands;
        });
    }

    public async Task BeginCompensation(
        Guid sagaId,
        OrderSagaStep origin,
        Event trigger,
        CancellationToken cancellationToken = default)
    {
        await _sagaStore.ExecuteAsync(async () =>
        {
            var saga = await _sagaStore.GetOrderSagaBySagaId(sagaId, cancellationToken);
            if (saga?.OrderSagaState is null)
            {
                return [];
            }

            var currentStep = Enum.Parse<OrderSagaStep>(saga.CurrentStep);
            var snapshot = SnapshotFromSaga(saga, currentStep);
            var result = OrderSagaStateMachine.BeginCompensation(snapshot, origin, trigger);
            if (!result.Changed)
            {
                return [];
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            ApplyTransitionToSaga(saga, result, now);
            _timeoutScheduler.Apply(saga, now);
            saga.Transitions.Add(new SagaTransition
            {
                SagaId = saga.SagaId,
                FromStep = currentStep.ToString(),
                ToStep = result.State.CurrentStep.ToString(),
                Timestamp = now,
                TriggerMessageId = trigger.Id,
                TriggerKind = SagaTriggerKind.Timeout,
                Error = "Saga step exceeded max retries; compensation started."
            });

            await _sagaStore.SaveChangesAsync(cancellationToken);

            SagaTelemetry.Compensation.Add(1,
                new KeyValuePair<string, object?>("type", saga.SagaType));
            LogSagaCompensating(_logger, saga.SagaId, saga.SagaType, currentStep.ToString(), origin.ToString());

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

    private static OrderSagaStateSnapshot SnapshotFromSaga(SagaInstance saga, OrderSagaStep currentStep) =>
        new(
            saga.SagaId,
            saga.OrderSagaState!.OrderId,
            currentStep,
            saga.Status,
            saga.OrderSagaState.LastStepResult,
            saga.OrderSagaState.Amount,
            ParseStep(saga.OrderSagaState.CompensationOrigin));

    private static void ApplyTransitionToSaga(
        SagaInstance saga,
        TransitionResult<OrderSagaStateSnapshot> result,
        DateTime now)
    {
        saga.CurrentStep = result.State.CurrentStep.ToString();
        saga.Status = result.State.Status;
        saga.UpdatedAt = now;
        saga.OrderSagaState!.LastStepResult = result.State.LastStepResult;
        saga.OrderSagaState.Amount = result.State.Amount;
        saga.OrderSagaState.CompensationOrigin = result.State.CompensationOrigin?.ToString();
        saga.LastCommandId = result.Commands.Count == 0 ? null : result.Commands[0].Id;
    }

    private static OrderSagaStep? ParseStep(string? value) =>
        Enum.TryParse<OrderSagaStep>(value, out var parsed) ? parsed : null;

    private static string? ExtractError(Event @event) => @event switch
    {
        StockReservationFailedEvent => "Stock reservation failed.",
        PaymentFailedEvent failed => $"Payment failed: {failed.Reason}",
        ShipmentFailedEvent failed => $"Shipment failed: {failed.Reason}",
        _ => null
    };

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

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Saga {SagaId} ({SagaType}) step {Step} exceeded retries; compensating from {Origin}")]
    private static partial void LogSagaCompensating(
        ILogger logger,
        Guid sagaId,
        string sagaType,
        string step,
        string origin);
}
