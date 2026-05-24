using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.Abstractions;

namespace Saga.Service.Domain.OrderSaga;

internal static class OrderSagaStateMachine
{
    public static TransitionResult<OrderSagaStateSnapshot> Transition(OrderSagaStateSnapshot state, Event trigger)
    {
        return trigger switch
        {
            OrderCreatedEvent orderCreated => OnOrderCreated(state, orderCreated),
            StockReservedEvent stockReserved => OnStockReserved(state, stockReserved),
            StockReservationFailedEvent reservationFailed => OnStockReservationFailed(state, reservationFailed),
            PaymentAuthorizedEvent paymentAuthorized => OnPaymentAuthorized(state, paymentAuthorized),
            PaymentFailedEvent paymentFailed => OnPaymentFailed(state, paymentFailed),
            OrderConfirmedEvent orderConfirmed => OnOrderConfirmed(state, orderConfirmed),
            StockCommittedEvent stockCommitted => OnStockCommitted(state, stockCommitted),
            ShipmentCreatedEvent shipmentCreated => OnShipmentCreated(state, shipmentCreated),
            ShipmentFailedEvent shipmentFailed => OnShipmentFailed(state, shipmentFailed),
            StockReleasedEvent stockReleased => OnCompensationReply(state, stockReleased, OrderSagaStep.ReleasingStock),
            PaymentVoidedEvent paymentVoided => OnCompensationReply(state, paymentVoided, OrderSagaStep.VoidingPayment),
            PaymentRefundedEvent paymentRefunded => OnCompensationReply(state, paymentRefunded, OrderSagaStep.RefundingPayment),
            OrderCancelledEvent orderCancelled => OnCompensationReply(state, orderCancelled, OrderSagaStep.CancellingOrder),
            ShipmentCancelledEvent shipmentCancelled => OnCompensationReply(state, shipmentCancelled, OrderSagaStep.CancellingShipment),
            _ => NoChange(state)
        };
    }

    // Begin compensation for the given last-completed step. Caller maps origin
    // from the saga's current step (failure events) or supplies it directly
    // (operator-driven abort, reaper escalation).
    public static TransitionResult<OrderSagaStateSnapshot> BeginCompensation(
        OrderSagaStateSnapshot state,
        OrderSagaStep origin,
        Event trigger)
    {
        if (state.Status != SagaStatus.Running)
        {
            return NoChange(state);
        }

        var sequence = GetCompensationSequence(origin);
        if (sequence.Count == 0)
        {
            var failed = state with
            {
                Status = SagaStatus.Failed,
                LastStepResult = trigger.GetType().Name
            };
            return new TransitionResult<OrderSagaStateSnapshot>(failed, [], Changed: true);
        }

        var firstStep = sequence[0];
        var command = BuildCompensationCommand(firstStep, state, trigger.Id);
        var next = state with
        {
            Status = SagaStatus.Compensating,
            CurrentStep = firstStep,
            CompensationOrigin = origin,
            LastStepResult = command.GetType().Name
        };

        return new TransitionResult<OrderSagaStateSnapshot>(next, [command], Changed: true);
    }

    public static OrderSagaStep GetLastCompletedStep(OrderSagaStep currentStep) =>
        currentStep switch
        {
            OrderSagaStep.StockReserving => OrderSagaStep.Started,
            OrderSagaStep.PaymentAuthorizing => OrderSagaStep.StockReserved,
            OrderSagaStep.OrderConfirming => OrderSagaStep.PaymentAuthorized,
            OrderSagaStep.StockCommitting => OrderSagaStep.OrderConfirmed,
            OrderSagaStep.ShipmentCreating => OrderSagaStep.StockCommitted,
            _ => currentStep
        };

    private static TransitionResult<OrderSagaStateSnapshot> OnOrderCreated(
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

        return new TransitionResult<OrderSagaStateSnapshot>(next, [command], Changed: true);
    }

    private static TransitionResult<OrderSagaStateSnapshot> OnStockReserved(
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
            LastStepResult = nameof(AuthorizePaymentCommand),
            Amount = @event.Amount
        };

        return new TransitionResult<OrderSagaStateSnapshot>(next, [command], Changed: true);
    }

    private static TransitionResult<OrderSagaStateSnapshot> OnPaymentAuthorized(
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

        return new TransitionResult<OrderSagaStateSnapshot>(next, [command], Changed: true);
    }

    private static TransitionResult<OrderSagaStateSnapshot> OnOrderConfirmed(
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

        return new TransitionResult<OrderSagaStateSnapshot>(next, [command], Changed: true);
    }

    private static TransitionResult<OrderSagaStateSnapshot> OnStockCommitted(
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

        return new TransitionResult<OrderSagaStateSnapshot>(next, [command], Changed: true);
    }

    private static TransitionResult<OrderSagaStateSnapshot> OnShipmentCreated(
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

        return new TransitionResult<OrderSagaStateSnapshot>(next, [], Changed: true);
    }

    private static TransitionResult<OrderSagaStateSnapshot> OnStockReservationFailed(
        OrderSagaStateSnapshot state,
        StockReservationFailedEvent @event)
    {
        if (!IsExpectedReply(state, OrderSagaStep.StockReserving, @event.OrderId, @event.SagaId))
        {
            return NoChange(state);
        }

        return BeginCompensation(state, OrderSagaStep.Started, @event);
    }

    private static TransitionResult<OrderSagaStateSnapshot> OnPaymentFailed(
        OrderSagaStateSnapshot state,
        PaymentFailedEvent @event)
    {
        if (!IsExpectedReply(state, OrderSagaStep.PaymentAuthorizing, @event.OrderId, @event.SagaId))
        {
            return NoChange(state);
        }

        return BeginCompensation(state, OrderSagaStep.StockReserved, @event);
    }

    private static TransitionResult<OrderSagaStateSnapshot> OnShipmentFailed(
        OrderSagaStateSnapshot state,
        ShipmentFailedEvent @event)
    {
        if (!IsExpectedReply(state, OrderSagaStep.ShipmentCreating, @event.OrderId, @event.SagaId))
        {
            return NoChange(state);
        }

        return BeginCompensation(state, OrderSagaStep.StockCommitted, @event);
    }

    // Compensation replies advance through the sequence keyed by CompensationOrigin.
    // If the saga is past `expectedStep`, the reply is dropped (at-least-once redelivery).
    // If a reply arrives without a known sequence, the saga parks in Failed.
    private static TransitionResult<OrderSagaStateSnapshot> OnCompensationReply(
        OrderSagaStateSnapshot state,
        Event trigger,
        OrderSagaStep expectedStep)
    {
        if (state.Status != SagaStatus.Compensating || state.CurrentStep != expectedStep)
        {
            return NoChange(state);
        }

        if (state.CompensationOrigin is not { } origin)
        {
            var failed = state with
            {
                Status = SagaStatus.Failed,
                LastStepResult = trigger.GetType().Name
            };
            return new TransitionResult<OrderSagaStateSnapshot>(failed, [], Changed: true);
        }

        var sequence = GetCompensationSequence(origin);
        var index = IndexOf(sequence, expectedStep);
        if (index < 0)
        {
            return NoChange(state);
        }

        if (index + 1 >= sequence.Count)
        {
            var completed = state with
            {
                CurrentStep = OrderSagaStep.Compensated,
                Status = SagaStatus.Compensated,
                LastStepResult = trigger.GetType().Name
            };
            return new TransitionResult<OrderSagaStateSnapshot>(completed, [], Changed: true);
        }

        var nextStep = sequence[index + 1];
        var command = BuildCompensationCommand(nextStep, state, trigger.Id);
        var next = state with
        {
            CurrentStep = nextStep,
            LastStepResult = command.GetType().Name
        };

        return new TransitionResult<OrderSagaStateSnapshot>(next, [command], Changed: true);
    }

    private static IReadOnlyList<OrderSagaStep> GetCompensationSequence(OrderSagaStep origin) =>
        origin switch
        {
            OrderSagaStep.Started =>
            [
                OrderSagaStep.CancellingOrder
            ],
            OrderSagaStep.StockReserved =>
            [
                OrderSagaStep.ReleasingStock,
                OrderSagaStep.CancellingOrder
            ],
            OrderSagaStep.PaymentAuthorized =>
            [
                OrderSagaStep.VoidingPayment,
                OrderSagaStep.ReleasingStock,
                OrderSagaStep.CancellingOrder
            ],
            OrderSagaStep.OrderConfirmed =>
            [
                OrderSagaStep.VoidingPayment,
                OrderSagaStep.ReleasingStock,
                OrderSagaStep.CancellingOrder
            ],
            OrderSagaStep.StockCommitted =>
            [
                OrderSagaStep.RefundingPayment,
                OrderSagaStep.CancellingOrder
            ],
            OrderSagaStep.ShipmentCreated =>
            [
                OrderSagaStep.CancellingShipment,
                OrderSagaStep.RefundingPayment,
                OrderSagaStep.CancellingOrder
            ],
            _ => []
        };

    private static Command BuildCompensationCommand(
        OrderSagaStep step,
        OrderSagaStateSnapshot state,
        Guid causationId)
    {
        return step switch
        {
            OrderSagaStep.ReleasingStock => new ReleaseStockCommand(state.OrderId, causationId, state.SagaId),
            OrderSagaStep.VoidingPayment => new VoidPaymentCommand(state.OrderId, "Saga compensation.", causationId, state.SagaId),
            OrderSagaStep.RefundingPayment => new RefundPaymentCommand(state.OrderId, state.Amount ?? 0m, causationId, state.SagaId),
            OrderSagaStep.CancellingOrder => new CancelOrderCommand(state.OrderId, causationId, state.SagaId),
            OrderSagaStep.CancellingShipment => new CancelShipmentCommand(state.OrderId, "Saga compensation.", causationId, state.SagaId),
            _ => throw new InvalidOperationException($"No compensation command for step {step}.")
        };
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

    private static TransitionResult<OrderSagaStateSnapshot> NoChange(OrderSagaStateSnapshot state) =>
        new(state, [], Changed: false);

    private static int IndexOf(IReadOnlyList<OrderSagaStep> sequence, OrderSagaStep step)
    {
        for (var i = 0; i < sequence.Count; i++)
        {
            if (sequence[i] == step)
            {
                return i;
            }
        }
        return -1;
    }
}
