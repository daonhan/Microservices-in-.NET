using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Reaper;
using Saga.Service.Models;
using Saga.Service.StateMachines;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class OrderSagaReplyProcessor
{
    private readonly SagaContext _sagaContext;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly OrderSagaTimeoutScheduler _timeoutScheduler;

    public OrderSagaReplyProcessor(
        SagaContext sagaContext,
        IOutboxUnitOfWork outboxUnitOfWork,
        TimeProvider timeProvider,
        OrderSagaTimeoutScheduler timeoutScheduler)
    {
        _sagaContext = sagaContext;
        _outboxUnitOfWork = outboxUnitOfWork;
        _timeProvider = timeProvider;
        _timeoutScheduler = timeoutScheduler;
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
                .Include(s => s.OrderSagaState)
                .FirstOrDefaultAsync(s => s.SagaId == sagaId);
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

            await _sagaContext.SaveChangesAsync();

            return result.Commands;
        });
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
}
