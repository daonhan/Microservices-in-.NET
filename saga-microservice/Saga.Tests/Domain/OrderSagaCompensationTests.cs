using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;
using Saga.Service.IntegrationEvents;
using Saga.Service.Models;
using Saga.Service.StateMachines;

namespace Saga.Tests.Domain;

public class OrderSagaCompensationTests
{
    [Fact]
    public void Given_PaymentAuthorizing_When_PaymentFailed_Then_StartsCompensationFromStockReserved()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var state = new OrderSagaStateSnapshot(
            sagaId, orderId, OrderSagaStep.PaymentAuthorizing, SagaStatus.Running);
        var paymentFailed = new PaymentFailedEvent(Guid.NewGuid(), orderId, "customer-1", "Declined")
        {
            SagaId = sagaId,
            CausationId = Guid.NewGuid()
        };

        var result = OrderSagaStateMachine.Transition(state, paymentFailed);

        Assert.True(result.Changed);
        Assert.Equal(SagaStatus.Compensating, result.State.Status);
        Assert.Equal(OrderSagaStep.ReleasingStock, result.State.CurrentStep);
        Assert.Equal(OrderSagaStep.StockReserved, result.State.CompensationOrigin);
        Assert.IsType<ReleaseStockCommand>(Assert.Single(result.Commands));
    }

    [Fact]
    public void Given_LastCompletedPaymentAuthorized_When_BeginCompensation_Then_EmitsVoidThenRelease()
    {
        var snapshot = CompensatingSnapshot();
        var trigger = new PaymentFailedEvent(Guid.NewGuid(), snapshot.OrderId, "c", "x")
        {
            SagaId = snapshot.SagaId,
            CausationId = Guid.NewGuid()
        };

        var result = OrderSagaStateMachine.BeginCompensation(snapshot, OrderSagaStep.PaymentAuthorized, trigger);

        Assert.True(result.Changed);
        Assert.Equal(OrderSagaStep.VoidingPayment, result.State.CurrentStep);
        Assert.IsType<VoidPaymentCommand>(Assert.Single(result.Commands));

        var afterVoid = new PaymentVoidedEvent(Guid.NewGuid(), snapshot.OrderId, "c", "Saga compensation.")
        {
            SagaId = snapshot.SagaId,
            CausationId = Guid.NewGuid()
        };
        var step2 = OrderSagaStateMachine.Transition(result.State, afterVoid);

        Assert.Equal(OrderSagaStep.ReleasingStock, step2.State.CurrentStep);
        Assert.IsType<ReleaseStockCommand>(Assert.Single(step2.Commands));

        var afterRelease = new StockReleasedEvent(snapshot.OrderId, [])
        {
            SagaId = snapshot.SagaId,
            CausationId = Guid.NewGuid()
        };
        var step3 = OrderSagaStateMachine.Transition(step2.State, afterRelease);

        Assert.Equal(SagaStatus.Compensated, step3.State.Status);
        Assert.Equal(OrderSagaStep.Compensated, step3.State.CurrentStep);
        Assert.Empty(step3.Commands);
    }

    [Fact]
    public void Given_LastCompletedOrderConfirmed_When_BeginCompensation_Then_EmitsVoidReleaseCancelOrder()
    {
        var snapshot = CompensatingSnapshot();
        var trigger = SyntheticTrigger();

        var step1 = OrderSagaStateMachine.BeginCompensation(snapshot, OrderSagaStep.OrderConfirmed, trigger);
        Assert.Equal(OrderSagaStep.VoidingPayment, step1.State.CurrentStep);
        Assert.IsType<VoidPaymentCommand>(Assert.Single(step1.Commands));

        var afterVoid = new PaymentVoidedEvent(Guid.NewGuid(), snapshot.OrderId, "c", "x")
        {
            SagaId = snapshot.SagaId,
            CausationId = Guid.NewGuid()
        };
        var step2 = OrderSagaStateMachine.Transition(step1.State, afterVoid);
        Assert.Equal(OrderSagaStep.ReleasingStock, step2.State.CurrentStep);
        Assert.IsType<ReleaseStockCommand>(Assert.Single(step2.Commands));

        var afterRelease = new StockReleasedEvent(snapshot.OrderId, [])
        {
            SagaId = snapshot.SagaId,
            CausationId = Guid.NewGuid()
        };
        var step3 = OrderSagaStateMachine.Transition(step2.State, afterRelease);
        Assert.Equal(OrderSagaStep.CancellingOrder, step3.State.CurrentStep);
        Assert.IsType<CancelOrderCommand>(Assert.Single(step3.Commands));

        var afterCancel = new OrderCancelledEvent(snapshot.OrderId, "c")
        {
            SagaId = snapshot.SagaId,
            CausationId = Guid.NewGuid()
        };
        var step4 = OrderSagaStateMachine.Transition(step3.State, afterCancel);
        Assert.Equal(SagaStatus.Compensated, step4.State.Status);
        Assert.Equal(OrderSagaStep.Compensated, step4.State.CurrentStep);
    }

    [Fact]
    public void Given_ShipmentCreating_When_ShipmentFailed_Then_EmitsRefundThenCancelOrder()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var snapshot = new OrderSagaStateSnapshot(
            sagaId, orderId, OrderSagaStep.ShipmentCreating, SagaStatus.Running, Amount: 25m);
        var shipmentFailed = new ShipmentFailedEvent(
            Guid.NewGuid(), orderId, "c", null, null, "Carrier down", DateTime.UtcNow)
        {
            SagaId = sagaId,
            CausationId = Guid.NewGuid()
        };

        var step1 = OrderSagaStateMachine.Transition(snapshot, shipmentFailed);

        Assert.True(step1.Changed);
        Assert.Equal(SagaStatus.Compensating, step1.State.Status);
        Assert.Equal(OrderSagaStep.RefundingPayment, step1.State.CurrentStep);
        Assert.Equal(OrderSagaStep.StockCommitted, step1.State.CompensationOrigin);
        var refund = Assert.IsType<RefundPaymentCommand>(Assert.Single(step1.Commands));
        Assert.Equal(25m, refund.Amount);

        var afterRefund = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 25m)
        {
            SagaId = sagaId,
            CausationId = Guid.NewGuid()
        };
        var step2 = OrderSagaStateMachine.Transition(step1.State, afterRefund);
        Assert.Equal(OrderSagaStep.CancellingOrder, step2.State.CurrentStep);
        Assert.IsType<CancelOrderCommand>(Assert.Single(step2.Commands));

        var afterCancel = new OrderCancelledEvent(orderId, "c")
        {
            SagaId = sagaId,
            CausationId = Guid.NewGuid()
        };
        var step3 = OrderSagaStateMachine.Transition(step2.State, afterCancel);
        Assert.Equal(SagaStatus.Compensated, step3.State.Status);
    }

    [Fact]
    public void Given_LastCompletedShipmentCreated_When_BeginCompensation_Then_EmitsCancelShipmentRefundCancelOrder()
    {
        var snapshot = CompensatingSnapshot() with { Amount = 50m };
        var trigger = SyntheticTrigger();

        var step1 = OrderSagaStateMachine.BeginCompensation(snapshot, OrderSagaStep.ShipmentCreated, trigger);
        Assert.Equal(OrderSagaStep.CancellingShipment, step1.State.CurrentStep);
        Assert.IsType<CancelShipmentCommand>(Assert.Single(step1.Commands));

        var afterCancelShip = new ShipmentCancelledEvent(
            Guid.NewGuid(), snapshot.OrderId, "c", DateTime.UtcNow, "saga")
        {
            SagaId = snapshot.SagaId,
            CausationId = Guid.NewGuid()
        };
        var step2 = OrderSagaStateMachine.Transition(step1.State, afterCancelShip);
        Assert.Equal(OrderSagaStep.RefundingPayment, step2.State.CurrentStep);
        var refund = Assert.IsType<RefundPaymentCommand>(Assert.Single(step2.Commands));
        Assert.Equal(50m, refund.Amount);

        var afterRefund = new PaymentRefundedEvent(Guid.NewGuid(), snapshot.OrderId, 50m)
        {
            SagaId = snapshot.SagaId,
            CausationId = Guid.NewGuid()
        };
        var step3 = OrderSagaStateMachine.Transition(step2.State, afterRefund);
        Assert.Equal(OrderSagaStep.CancellingOrder, step3.State.CurrentStep);

        var afterCancel = new OrderCancelledEvent(snapshot.OrderId, "c")
        {
            SagaId = snapshot.SagaId,
            CausationId = Guid.NewGuid()
        };
        var step4 = OrderSagaStateMachine.Transition(step3.State, afterCancel);
        Assert.Equal(SagaStatus.Compensated, step4.State.Status);
    }

    [Fact]
    public void Given_StockReserving_When_StockReservationFailed_Then_CompletesCompensationOnOrderCancelled()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var snapshot = new OrderSagaStateSnapshot(
            sagaId, orderId, OrderSagaStep.StockReserving, SagaStatus.Running);
        var reservationFailed = new StockReservationFailedEvent(
            orderId, [new FailedItem(101, 2, 0)])
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };

        var step1 = OrderSagaStateMachine.Transition(snapshot, reservationFailed);
        Assert.Equal(OrderSagaStep.CancellingOrder, step1.State.CurrentStep);
        Assert.IsType<CancelOrderCommand>(Assert.Single(step1.Commands));

        var afterCancel = new OrderCancelledEvent(orderId, "c")
        {
            SagaId = sagaId,
            CausationId = Guid.NewGuid()
        };
        var step2 = OrderSagaStateMachine.Transition(step1.State, afterCancel);

        Assert.Equal(SagaStatus.Compensated, step2.State.Status);
        Assert.Equal(OrderSagaStep.Compensated, step2.State.CurrentStep);
        Assert.Empty(step2.Commands);
    }

    [Fact]
    public void Given_CompensatingWithoutOrigin_When_StockReleasedArrives_Then_ParksSagaInFailed()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var snapshot = new OrderSagaStateSnapshot(
            sagaId, orderId, OrderSagaStep.ReleasingStock, SagaStatus.Compensating);
        var stockReleased = new StockReleasedEvent(orderId, [])
        {
            SagaId = sagaId,
            CausationId = Guid.NewGuid()
        };

        var result = OrderSagaStateMachine.Transition(snapshot, stockReleased);

        Assert.True(result.Changed);
        Assert.Equal(SagaStatus.Failed, result.State.Status);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public void Given_ReleasingStock_When_StockReleasedReplayed_Then_NoOps()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var snapshot = new OrderSagaStateSnapshot(
            sagaId,
            orderId,
            OrderSagaStep.Compensated,
            SagaStatus.Compensated,
            CompensationOrigin: OrderSagaStep.StockReserved);
        var stockReleased = new StockReleasedEvent(orderId, [])
        {
            SagaId = sagaId,
            CausationId = Guid.NewGuid()
        };

        var result = OrderSagaStateMachine.Transition(snapshot, stockReleased);

        Assert.False(result.Changed);
        Assert.Empty(result.Commands);
    }

    private static OrderSagaStateSnapshot CompensatingSnapshot() =>
        new(
            SagaId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CurrentStep: OrderSagaStep.PaymentAuthorizing,
            Status: SagaStatus.Running,
            Amount: 25m);

    private static Event SyntheticTrigger() => new()
    {
        CausationId = Guid.NewGuid(),
        SagaId = Guid.NewGuid()
    };
}
