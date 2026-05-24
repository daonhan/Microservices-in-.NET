using ECommerce.Shared.IntegrationEvents.Commands;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Models;
using Saga.Service.StateMachines;

namespace Saga.Tests.Domain;

public class RefundSagaStateMachineTests
{
    private static RefundSagaStateSnapshot NewState(
        Guid sagaId,
        Guid orderId,
        Guid? shipmentId,
        RefundSagaStep step,
        SagaStatus status = SagaStatus.Running) =>
        new(
            sagaId,
            orderId,
            Guid.NewGuid(),
            shipmentId,
            42.50m,
            "USD",
            step,
            status);

    [Fact]
    public void Given_Started_When_RefundRequested_Then_TransitionsToPaymentRefundingAndEmitsRefundCommand()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var requested = new RefundRequestedEvent(orderId, Guid.NewGuid(), Guid.NewGuid(), 42.50m, "USD")
        {
            CorrelationId = correlationId
        };
        var state = NewState(sagaId, orderId, requested.ShipmentId, RefundSagaStep.Started);

        var result = RefundSagaStateMachine.Transition(state, requested);

        Assert.True(result.Changed);
        Assert.Equal(RefundSagaStep.PaymentRefunding, result.State.CurrentStep);
        var command = Assert.IsType<RefundPaymentCommand>(Assert.Single(result.Commands));
        Assert.Equal(orderId, command.OrderId);
        Assert.Equal(42.50m, command.Amount);
        Assert.Equal(requested.Id, command.CausationId);
        Assert.Equal(sagaId, command.SagaId);
        Assert.Equal(correlationId, command.CorrelationId);
    }

    [Fact]
    public void Given_PaymentRefunding_When_PaymentRefundedWithShipment_Then_AdvancesAndEmitsCancelShipmentCommand()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var refunded = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 42.50m)
        {
            SagaId = sagaId
        };
        var state = NewState(sagaId, orderId, shipmentId, RefundSagaStep.PaymentRefunding);

        var result = RefundSagaStateMachine.Transition(state, refunded);

        Assert.True(result.Changed);
        Assert.Equal(RefundSagaStep.ShipmentCancellingOrReturning, result.State.CurrentStep);
        var command = Assert.IsType<CancelShipmentCommand>(Assert.Single(result.Commands));
        Assert.Equal(orderId, command.OrderId);
        Assert.Equal(refunded.Id, command.CausationId);
        Assert.Equal(sagaId, command.SagaId);
    }

    [Fact]
    public void Given_PaymentRefunding_When_PaymentRefundedWithoutShipment_Then_CompletesSaga()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var refunded = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 42.50m)
        {
            SagaId = sagaId
        };
        var state = NewState(sagaId, orderId, shipmentId: null, RefundSagaStep.PaymentRefunding);

        var result = RefundSagaStateMachine.Transition(state, refunded);

        Assert.True(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(RefundSagaStep.Completed, result.State.CurrentStep);
        Assert.Equal(SagaStatus.Completed, result.State.Status);
    }

    [Fact]
    public void Given_PaymentRefunding_When_PaymentFailed_Then_ParksSagaInFailed()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var failed = new PaymentFailedEvent(Guid.NewGuid(), orderId, "customer-1", "Refund declined")
        {
            SagaId = sagaId
        };
        var state = NewState(sagaId, orderId, Guid.NewGuid(), RefundSagaStep.PaymentRefunding);

        var result = RefundSagaStateMachine.Transition(state, failed);

        Assert.True(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(SagaStatus.Failed, result.State.Status);
        Assert.Equal(nameof(PaymentFailedEvent), result.State.LastStepResult);
    }

    [Fact]
    public void Given_ShipmentCancelling_When_ShipmentCancelled_Then_CompletesSaga()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var cancelled = new ShipmentCancelledEvent(Guid.NewGuid(), orderId, "customer-1", DateTime.UtcNow, "Refund")
        {
            SagaId = sagaId
        };
        var state = NewState(sagaId, orderId, Guid.NewGuid(), RefundSagaStep.ShipmentCancellingOrReturning);

        var result = RefundSagaStateMachine.Transition(state, cancelled);

        Assert.True(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(RefundSagaStep.Completed, result.State.CurrentStep);
        Assert.Equal(SagaStatus.Completed, result.State.Status);
    }

    [Fact]
    public void Given_ShipmentCancelling_When_ShipmentFailed_Then_CompensatesAndEmitsCancelOrderCommand()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shipmentFailed = new ShipmentFailedEvent(
            Guid.NewGuid(), orderId, "customer-1", null, null, "Carrier down", DateTime.UtcNow)
        {
            SagaId = sagaId
        };
        var state = NewState(sagaId, orderId, Guid.NewGuid(), RefundSagaStep.ShipmentCancellingOrReturning);

        var result = RefundSagaStateMachine.Transition(state, shipmentFailed);

        Assert.True(result.Changed);
        Assert.Equal(RefundSagaStep.CancellingOrder, result.State.CurrentStep);
        Assert.Equal(SagaStatus.Compensating, result.State.Status);
        var command = Assert.IsType<CancelOrderCommand>(Assert.Single(result.Commands));
        Assert.Equal(orderId, command.OrderId);
        Assert.Equal(shipmentFailed.Id, command.CausationId);
        Assert.Equal(sagaId, command.SagaId);
    }

    [Fact]
    public void Given_CancellingOrder_When_OrderCancelled_Then_SagaCompensated()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderCancelled = new OrderCancelledEvent(orderId, "customer-1")
        {
            SagaId = sagaId
        };
        var state = NewState(
            sagaId, orderId, Guid.NewGuid(), RefundSagaStep.CancellingOrder, SagaStatus.Compensating);

        var result = RefundSagaStateMachine.Transition(state, orderCancelled);

        Assert.True(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(RefundSagaStep.Compensated, result.State.CurrentStep);
        Assert.Equal(SagaStatus.Compensated, result.State.Status);
    }

    [Fact]
    public void Given_ShipmentCancelling_When_PaymentRefundedReplayed_Then_NoOps()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var refunded = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 42.50m)
        {
            SagaId = sagaId
        };
        var state = NewState(sagaId, orderId, Guid.NewGuid(), RefundSagaStep.ShipmentCancellingOrReturning);

        var result = RefundSagaStateMachine.Transition(state, refunded);

        Assert.False(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Given_AnyState_When_UnrelatedEvent_Then_NoOps()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var unrelated = new OrderCreatedEvent(orderId, "customer-1", [new OrderItem("101", 1)]);
        var state = NewState(sagaId, orderId, Guid.NewGuid(), RefundSagaStep.PaymentRefunding);

        var result = RefundSagaStateMachine.Transition(state, unrelated);

        Assert.False(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(state, result.State);
    }
}
