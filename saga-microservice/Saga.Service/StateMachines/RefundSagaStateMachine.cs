using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Models;

namespace Saga.Service.StateMachines;

// Refund saga: Started -> PaymentRefunding -> (PaymentRefunded) ->
// ShipmentCancellingOrReturning -> Completed. If the refund succeeds but the
// shipment action fails, the saga compensates by cancelling the order so the
// system stays consistent with the money already returned to the customer.
internal static class RefundSagaStateMachine
{
    private const string ShipmentCancelReason = "Refund saga: cancelling shipment for refunded order.";

    public static RefundSagaTransitionResult Transition(RefundSagaStateSnapshot state, Event trigger)
    {
        return trigger switch
        {
            RefundRequestedEvent requested => OnRefundRequested(state, requested),
            PaymentRefundedEvent refunded => OnPaymentRefunded(state, refunded),
            PaymentFailedEvent failed => OnPaymentFailed(state, failed),
            ShipmentCancelledEvent cancelled => OnShipmentCancelled(state, cancelled),
            ShipmentFailedEvent shipmentFailed => OnShipmentFailed(state, shipmentFailed),
            OrderCancelledEvent orderCancelled => OnOrderCancelled(state, orderCancelled),
            _ => NoChange(state)
        };
    }

    private static RefundSagaTransitionResult OnRefundRequested(
        RefundSagaStateSnapshot state,
        RefundRequestedEvent @event)
    {
        if (state.CurrentStep != RefundSagaStep.Started || state.Status != SagaStatus.Running)
        {
            return NoChange(state);
        }

        var command = new RefundPaymentCommand(
            @event.OrderId,
            @event.RefundAmount,
            @event.Id,
            state.SagaId)
        {
            CorrelationId = @event.CorrelationId
        };

        var next = state with
        {
            CurrentStep = RefundSagaStep.PaymentRefunding,
            LastStepResult = nameof(RefundPaymentCommand)
        };

        return new RefundSagaTransitionResult(next, [command], Changed: true);
    }

    private static RefundSagaTransitionResult OnPaymentRefunded(
        RefundSagaStateSnapshot state,
        PaymentRefundedEvent @event)
    {
        if (!IsExpectedReply(state, RefundSagaStep.PaymentRefunding, @event.OrderId))
        {
            return NoChange(state);
        }

        // No shipment on the order: refund is the whole saga.
        if (state.ShipmentId is null)
        {
            var done = state with
            {
                CurrentStep = RefundSagaStep.Completed,
                Status = SagaStatus.Completed,
                LastStepResult = nameof(PaymentRefundedEvent)
            };
            return new RefundSagaTransitionResult(done, [], Changed: true);
        }

        var command = new CancelShipmentCommand(
            state.OrderId,
            ShipmentCancelReason,
            @event.Id,
            state.SagaId)
        {
            CorrelationId = @event.CorrelationId
        };

        var next = state with
        {
            CurrentStep = RefundSagaStep.ShipmentCancellingOrReturning,
            LastStepResult = nameof(CancelShipmentCommand)
        };

        return new RefundSagaTransitionResult(next, [command], Changed: true);
    }

    private static RefundSagaTransitionResult OnPaymentFailed(
        RefundSagaStateSnapshot state,
        PaymentFailedEvent @event)
    {
        if (!IsExpectedReply(state, RefundSagaStep.PaymentRefunding, @event.OrderId))
        {
            return NoChange(state);
        }

        // Refund itself failed: nothing was changed downstream, park the saga.
        var failed = state with
        {
            Status = SagaStatus.Failed,
            LastStepResult = nameof(PaymentFailedEvent)
        };
        return new RefundSagaTransitionResult(failed, [], Changed: true);
    }

    private static RefundSagaTransitionResult OnShipmentCancelled(
        RefundSagaStateSnapshot state,
        ShipmentCancelledEvent @event)
    {
        if (!IsExpectedReply(state, RefundSagaStep.ShipmentCancellingOrReturning, @event.OrderId))
        {
            return NoChange(state);
        }

        var completed = state with
        {
            CurrentStep = RefundSagaStep.Completed,
            Status = SagaStatus.Completed,
            LastStepResult = nameof(ShipmentCancelledEvent)
        };
        return new RefundSagaTransitionResult(completed, [], Changed: true);
    }

    private static RefundSagaTransitionResult OnShipmentFailed(
        RefundSagaStateSnapshot state,
        ShipmentFailedEvent @event)
    {
        if (!IsExpectedReply(state, RefundSagaStep.ShipmentCancellingOrReturning, @event.OrderId))
        {
            return NoChange(state);
        }

        // Money is already back with the customer; we cannot un-refund. Cancel
        // the order so its state reflects the completed refund.
        var command = new CancelOrderCommand(state.OrderId, @event.Id, state.SagaId)
        {
            CorrelationId = @event.CorrelationId
        };

        var next = state with
        {
            CurrentStep = RefundSagaStep.CancellingOrder,
            Status = SagaStatus.Compensating,
            LastStepResult = nameof(CancelOrderCommand)
        };

        return new RefundSagaTransitionResult(next, [command], Changed: true);
    }

    private static RefundSagaTransitionResult OnOrderCancelled(
        RefundSagaStateSnapshot state,
        OrderCancelledEvent @event)
    {
        if (state.Status != SagaStatus.Compensating
            || state.CurrentStep != RefundSagaStep.CancellingOrder
            || @event.OrderId != state.OrderId)
        {
            return NoChange(state);
        }

        var compensated = state with
        {
            CurrentStep = RefundSagaStep.Compensated,
            Status = SagaStatus.Compensated,
            LastStepResult = nameof(OrderCancelledEvent)
        };
        return new RefundSagaTransitionResult(compensated, [], Changed: true);
    }

    // A reply is acted on only when it matches the in-flight step for this saga.
    // Once the step advances, a redelivered reply fails this guard and is a
    // no-op, giving at-least-once idempotency.
    private static bool IsExpectedReply(
        RefundSagaStateSnapshot state,
        RefundSagaStep expectedStep,
        Guid eventOrderId) =>
        state.CurrentStep == expectedStep
        && state.Status == SagaStatus.Running
        && eventOrderId == state.OrderId;

    private static RefundSagaTransitionResult NoChange(RefundSagaStateSnapshot state) =>
        new(state, [], Changed: false);
}
