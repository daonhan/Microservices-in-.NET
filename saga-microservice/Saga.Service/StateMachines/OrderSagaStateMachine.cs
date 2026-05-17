using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;
using Saga.Service.IntegrationEvents;
using Saga.Service.Models;

namespace Saga.Service.StateMachines;

internal static class OrderSagaStateMachine
{
    public static OrderSagaTransitionResult Transition(OrderSagaStateSnapshot state, Event trigger)
    {
        return trigger switch
        {
            OrderCreatedEvent orderCreated => OnOrderCreated(state, orderCreated),
            StockReservedEvent stockReserved => OnStockReserved(state, stockReserved),
            StockReservationFailedEvent reservationFailed => OnStockReservationFailed(state, reservationFailed),
            _ => NoChange(state)
        };
    }

    private static OrderSagaTransitionResult OnStockReserved(
        OrderSagaStateSnapshot state,
        StockReservedEvent @event)
    {
        if (state.CurrentStep != OrderSagaStep.StockReserving
            || state.Status != SagaStatus.Running
            || @event.OrderId != state.OrderId
            || @event.SagaId != state.SagaId)
        {
            return NoChange(state);
        }

        var next = state with
        {
            CurrentStep = OrderSagaStep.StockReserved,
            LastStepResult = nameof(StockReservedEvent)
        };

        return new OrderSagaTransitionResult(next, [], Changed: true);
    }

    private static OrderSagaTransitionResult OnStockReservationFailed(
        OrderSagaStateSnapshot state,
        StockReservationFailedEvent @event)
    {
        if (state.CurrentStep != OrderSagaStep.StockReserving
            || state.Status != SagaStatus.Running
            || @event.OrderId != state.OrderId
            || @event.SagaId != state.SagaId)
        {
            return NoChange(state);
        }

        var next = state with
        {
            Status = SagaStatus.Failed,
            LastStepResult = nameof(StockReservationFailedEvent)
        };

        return new OrderSagaTransitionResult(next, [], Changed: true);
    }

    private static OrderSagaTransitionResult OnOrderCreated(
        OrderSagaStateSnapshot state,
        OrderCreatedEvent @event)
    {
        if (state.CurrentStep != OrderSagaStep.Started || state.Status != SagaStatus.Running)
        {
            return NoChange(state);
        }

        var command = new ReserveStockCommand(
            @event.OrderId,
            @event.CustomerId,
            @event.Items
                .Select(i => new ReserveStockItem(i.ProductId, i.Quantity, i.UnitPrice))
                .ToList(),
            @event.Currency,
            @event.Id,
            state.SagaId)
        {
            CorrelationId = @event.CorrelationId
        };

        var next = state with
        {
            CurrentStep = OrderSagaStep.StockReserving,
            LastStepResult = nameof(ReserveStockCommand)
        };

        return new OrderSagaTransitionResult(next, [command], Changed: true);
    }

    private static OrderSagaTransitionResult NoChange(OrderSagaStateSnapshot state) =>
        new(state, [], Changed: false);
}
