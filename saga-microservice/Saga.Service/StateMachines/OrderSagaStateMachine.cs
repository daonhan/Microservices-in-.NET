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
            PaymentAuthorizedEvent paymentAuthorized => OnPaymentAuthorized(state, paymentAuthorized),
            OrderConfirmedEvent orderConfirmed => OnOrderConfirmed(state, orderConfirmed),
            StockCommittedEvent stockCommitted => OnStockCommitted(state, stockCommitted),
            ShipmentCreatedEvent shipmentCreated => OnShipmentCreated(state, shipmentCreated),
            _ => NoChange(state)
        };
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

    private static OrderSagaTransitionResult OnStockReserved(
        OrderSagaStateSnapshot state,
        StockReservedEvent @event)
    {
        if (!IsExpectedReply(state, OrderSagaStep.StockReserving, @event.OrderId, @event.SagaId))
        {
            return NoChange(state);
        }

        var command = new AuthorizePaymentCommand(
            @event.OrderId,
            @event.Amount,
            @event.Currency,
            @event.Id,
            state.SagaId)
        {
            CorrelationId = @event.CorrelationId
        };

        var next = state with
        {
            CurrentStep = OrderSagaStep.PaymentAuthorizing,
            LastStepResult = nameof(AuthorizePaymentCommand)
        };

        return new OrderSagaTransitionResult(next, [command], Changed: true);
    }

    private static OrderSagaTransitionResult OnPaymentAuthorized(
        OrderSagaStateSnapshot state,
        PaymentAuthorizedEvent @event)
    {
        if (!IsExpectedReply(state, OrderSagaStep.PaymentAuthorizing, @event.OrderId, @event.SagaId))
        {
            return NoChange(state);
        }

        var command = new ConfirmOrderCommand(@event.OrderId, @event.Id, state.SagaId)
        {
            CorrelationId = @event.CorrelationId
        };

        var next = state with
        {
            CurrentStep = OrderSagaStep.OrderConfirming,
            LastStepResult = nameof(ConfirmOrderCommand)
        };

        return new OrderSagaTransitionResult(next, [command], Changed: true);
    }

    private static OrderSagaTransitionResult OnOrderConfirmed(
        OrderSagaStateSnapshot state,
        OrderConfirmedEvent @event)
    {
        if (!IsExpectedReply(state, OrderSagaStep.OrderConfirming, @event.OrderId, @event.SagaId))
        {
            return NoChange(state);
        }

        var command = new CommitStockCommand(@event.OrderId, @event.Id, state.SagaId)
        {
            CorrelationId = @event.CorrelationId
        };

        var next = state with
        {
            CurrentStep = OrderSagaStep.StockCommitting,
            LastStepResult = nameof(CommitStockCommand)
        };

        return new OrderSagaTransitionResult(next, [command], Changed: true);
    }

    private static OrderSagaTransitionResult OnStockCommitted(
        OrderSagaStateSnapshot state,
        StockCommittedEvent @event)
    {
        if (!IsExpectedReply(state, OrderSagaStep.StockCommitting, @event.OrderId, @event.SagaId))
        {
            return NoChange(state);
        }

        var command = new CreateShipmentCommand(
            @event.OrderId,
            @event.Items
                .Select(i => new CreateShipmentItem(i.ProductId, i.WarehouseId, i.Quantity))
                .ToList(),
            @event.Id,
            state.SagaId)
        {
            CorrelationId = @event.CorrelationId
        };

        var next = state with
        {
            CurrentStep = OrderSagaStep.ShipmentCreating,
            LastStepResult = nameof(CreateShipmentCommand)
        };

        return new OrderSagaTransitionResult(next, [command], Changed: true);
    }

    private static OrderSagaTransitionResult OnShipmentCreated(
        OrderSagaStateSnapshot state,
        ShipmentCreatedEvent @event)
    {
        if (!IsExpectedReply(state, OrderSagaStep.ShipmentCreating, @event.OrderId, @event.SagaId))
        {
            return NoChange(state);
        }

        var next = state with
        {
            CurrentStep = OrderSagaStep.Completed,
            Status = SagaStatus.Completed,
            LastStepResult = nameof(ShipmentCreatedEvent)
        };

        return new OrderSagaTransitionResult(next, [], Changed: true);
    }

    private static OrderSagaTransitionResult OnStockReservationFailed(
        OrderSagaStateSnapshot state,
        StockReservationFailedEvent @event)
    {
        if (!IsExpectedReply(state, OrderSagaStep.StockReserving, @event.OrderId, @event.SagaId))
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

    // A reply is acted on only when it matches the in-flight step for this saga.
    // Once the step has advanced, a redelivered reply (same SagaId/CausationId)
    // fails this guard and is a no-op, giving at-least-once idempotency.
    private static bool IsExpectedReply(
        OrderSagaStateSnapshot state,
        OrderSagaStep expectedStep,
        Guid eventOrderId,
        Guid? eventSagaId) =>
        state.CurrentStep == expectedStep
        && state.Status == SagaStatus.Running
        && eventOrderId == state.OrderId
        && eventSagaId == state.SagaId;

    private static OrderSagaTransitionResult NoChange(OrderSagaStateSnapshot state) =>
        new(state, [], Changed: false);
}
