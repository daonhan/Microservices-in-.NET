using ECommerce.Shared.IntegrationEvents.Commands;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Models;
using Saga.Service.StateMachines;

namespace Saga.Tests.Domain;

public class OrderSagaStateMachineTests
{
    [Fact]
    public void Given_Started_When_OrderCreated_Then_TransitionsToStockReservingAndEmitsReserveCommand()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderCreated = new OrderCreatedEvent(
            orderId,
            "customer-1",
            [new OrderItem("101", 2, 12.50m)],
            "USD");
        var state = new OrderSagaStateSnapshot(
            sagaId,
            orderId,
            OrderSagaStep.Started,
            SagaStatus.Running);

        var result = OrderSagaStateMachine.Transition(state, orderCreated);

        Assert.True(result.Changed);
        Assert.Equal(OrderSagaStep.StockReserving, result.State.CurrentStep);
        var command = Assert.IsType<ReserveStockCommand>(Assert.Single(result.Commands));
        Assert.Equal(orderId, command.OrderId);
        Assert.Equal(orderCreated.Id, command.CausationId);
        Assert.Equal(sagaId, command.SagaId);
    }

    [Fact]
    public void Given_StockReserving_When_StockReserved_Then_AdvancesToPaymentAuthorizingAndEmitsAuthorizeCommand()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var stockReserved = new StockReservedEvent(
            orderId,
            [new ReservedItem(101, 1, 2)],
            25m,
            "USD")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId,
            CorrelationId = correlationId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId,
            orderId,
            OrderSagaStep.StockReserving,
            SagaStatus.Running);

        var result = OrderSagaStateMachine.Transition(state, stockReserved);

        Assert.True(result.Changed);
        Assert.Equal(OrderSagaStep.PaymentAuthorizing, result.State.CurrentStep);
        var command = Assert.IsType<AuthorizePaymentCommand>(Assert.Single(result.Commands));
        Assert.Equal(orderId, command.OrderId);
        Assert.Equal(25m, command.Amount);
        Assert.Equal("USD", command.Currency);
        Assert.Equal(stockReserved.Id, command.CausationId);
        Assert.Equal(sagaId, command.SagaId);
        Assert.Equal(correlationId, command.CorrelationId);
    }

    [Fact]
    public void Given_PaymentAuthorizing_When_PaymentAuthorized_Then_AdvancesToOrderConfirmingAndEmitsConfirmCommand()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentAuthorized = new PaymentAuthorizedEvent(
            Guid.NewGuid(), orderId, "customer-1", 25m, "USD")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId, orderId, OrderSagaStep.PaymentAuthorizing, SagaStatus.Running);

        var result = OrderSagaStateMachine.Transition(state, paymentAuthorized);

        Assert.True(result.Changed);
        Assert.Equal(OrderSagaStep.OrderConfirming, result.State.CurrentStep);
        var command = Assert.IsType<ConfirmOrderCommand>(Assert.Single(result.Commands));
        Assert.Equal(orderId, command.OrderId);
        Assert.Equal(paymentAuthorized.Id, command.CausationId);
        Assert.Equal(sagaId, command.SagaId);
    }

    [Fact]
    public void Given_OrderConfirming_When_OrderConfirmed_Then_AdvancesToStockCommittingAndEmitsCommitCommand()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderConfirmed = new OrderConfirmedEvent(orderId, "customer-1")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId, orderId, OrderSagaStep.OrderConfirming, SagaStatus.Running);

        var result = OrderSagaStateMachine.Transition(state, orderConfirmed);

        Assert.True(result.Changed);
        Assert.Equal(OrderSagaStep.StockCommitting, result.State.CurrentStep);
        var command = Assert.IsType<CommitStockCommand>(Assert.Single(result.Commands));
        Assert.Equal(orderId, command.OrderId);
        Assert.Equal(orderConfirmed.Id, command.CausationId);
        Assert.Equal(sagaId, command.SagaId);
    }

    [Fact]
    public void Given_StockCommitting_When_StockCommitted_Then_AdvancesToShipmentCreatingAndEmitsCreateShipmentCommand()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var stockCommitted = new StockCommittedEvent(
            orderId, [new CommittedItem(101, 1, 2)])
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId, orderId, OrderSagaStep.StockCommitting, SagaStatus.Running);

        var result = OrderSagaStateMachine.Transition(state, stockCommitted);

        Assert.True(result.Changed);
        Assert.Equal(OrderSagaStep.ShipmentCreating, result.State.CurrentStep);
        var command = Assert.IsType<CreateShipmentCommand>(Assert.Single(result.Commands));
        Assert.Equal(orderId, command.OrderId);
        var item = Assert.Single(command.Items);
        Assert.Equal(101, item.ProductId);
        Assert.Equal(1, item.WarehouseId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(stockCommitted.Id, command.CausationId);
        Assert.Equal(sagaId, command.SagaId);
    }

    [Fact]
    public void Given_ShipmentCreating_When_ShipmentCreated_Then_CompletesSaga()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shipmentCreated = new ShipmentCreatedEvent(
            Guid.NewGuid(), orderId, "customer-1", 1, [new ShipmentLineItem(101, 2)])
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId, orderId, OrderSagaStep.ShipmentCreating, SagaStatus.Running);

        var result = OrderSagaStateMachine.Transition(state, shipmentCreated);

        Assert.True(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(OrderSagaStep.Completed, result.State.CurrentStep);
        Assert.Equal(SagaStatus.Completed, result.State.Status);
    }

    [Fact]
    public void Given_OrderConfirming_When_PaymentAuthorizedReplayed_Then_NoOps()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentAuthorized = new PaymentAuthorizedEvent(
            Guid.NewGuid(), orderId, "customer-1", 25m, "USD")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId,
            orderId,
            OrderSagaStep.OrderConfirming,
            SagaStatus.Running,
            nameof(ConfirmOrderCommand));

        var result = OrderSagaStateMachine.Transition(state, paymentAuthorized);

        Assert.False(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Given_StockReserved_When_StockReservedReplayed_Then_NoOps()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var stockReserved = new StockReservedEvent(
            orderId,
            [new ReservedItem(101, 1, 2)],
            25m,
            "USD")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId,
            orderId,
            OrderSagaStep.StockReserved,
            SagaStatus.Running,
            nameof(StockReservedEvent));

        var result = OrderSagaStateMachine.Transition(state, stockReserved);

        Assert.False(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Given_PaymentAuthorizing_When_PaymentFailed_Then_CompensationEndsWithCancelOrder()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentFailed = new PaymentFailedEvent(Guid.NewGuid(), orderId, "customer-1", "declined")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId,
            orderId,
            OrderSagaStep.PaymentAuthorizing,
            SagaStatus.Running);

        var afterFail = OrderSagaStateMachine.Transition(state, paymentFailed);
        Assert.IsType<ReleaseStockCommand>(Assert.Single(afterFail.Commands));
        Assert.Equal(OrderSagaStep.ReleasingStock, afterFail.State.CurrentStep);
        Assert.Equal(OrderSagaStep.StockReserved, afterFail.State.CompensationOrigin);

        var stockReleased = new StockReleasedEvent(orderId, [new ReleasedItem(101, 1, 2)])
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var afterRelease = OrderSagaStateMachine.Transition(afterFail.State, stockReleased);
        Assert.True(afterRelease.Changed);
        Assert.IsType<CancelOrderCommand>(Assert.Single(afterRelease.Commands));
        Assert.Equal(OrderSagaStep.CancellingOrder, afterRelease.State.CurrentStep);
        Assert.Equal(SagaStatus.Compensating, afterRelease.State.Status);

        var orderCancelled = new OrderCancelledEvent(orderId, "customer-1")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var afterCancel = OrderSagaStateMachine.Transition(afterRelease.State, orderCancelled);
        Assert.True(afterCancel.Changed);
        Assert.Empty(afterCancel.Commands);
        Assert.Equal(SagaStatus.Compensated, afterCancel.State.Status);
    }

    [Fact]
    public void Given_PaymentAuthorized_Origin_CompensationChain_EndsWithCancelOrder()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var trigger = new OrderCancelledEvent(orderId, "customer-1")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId,
            orderId,
            OrderSagaStep.OrderConfirming,
            SagaStatus.Running);

        var begin = OrderSagaStateMachine.BeginCompensation(
            state,
            OrderSagaStep.PaymentAuthorized,
            trigger);
        Assert.IsType<VoidPaymentCommand>(Assert.Single(begin.Commands));
        Assert.Equal(OrderSagaStep.VoidingPayment, begin.State.CurrentStep);

        var paymentVoided = new PaymentVoidedEvent(Guid.NewGuid(), orderId, "customer-1", "compensation")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var afterVoid = OrderSagaStateMachine.Transition(begin.State, paymentVoided);
        Assert.IsType<ReleaseStockCommand>(Assert.Single(afterVoid.Commands));
        Assert.Equal(OrderSagaStep.ReleasingStock, afterVoid.State.CurrentStep);

        var stockReleased = new StockReleasedEvent(orderId, [new ReleasedItem(101, 1, 2)])
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var afterRelease = OrderSagaStateMachine.Transition(afterVoid.State, stockReleased);
        Assert.IsType<CancelOrderCommand>(Assert.Single(afterRelease.Commands));
        Assert.Equal(OrderSagaStep.CancellingOrder, afterRelease.State.CurrentStep);

        var orderCancelled = new OrderCancelledEvent(orderId, "customer-1")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var afterCancel = OrderSagaStateMachine.Transition(afterRelease.State, orderCancelled);
        Assert.True(afterCancel.Changed);
        Assert.Empty(afterCancel.Commands);
        Assert.Equal(SagaStatus.Compensated, afterCancel.State.Status);
    }

    [Fact]
    public void Given_StockReserving_When_StockReservationFailed_Then_StartsCompensationFromStarted()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var reservationFailed = new StockReservationFailedEvent(
            orderId,
            [new FailedItem(101, 2, 0)])
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId
        };
        var state = new OrderSagaStateSnapshot(
            sagaId,
            orderId,
            OrderSagaStep.StockReserving,
            SagaStatus.Running);

        var result = OrderSagaStateMachine.Transition(state, reservationFailed);

        Assert.True(result.Changed);
        Assert.Equal(SagaStatus.Compensating, result.State.Status);
        Assert.Equal(OrderSagaStep.CancellingOrder, result.State.CurrentStep);
        Assert.Equal(OrderSagaStep.Started, result.State.CompensationOrigin);
        Assert.IsType<CancelOrderCommand>(Assert.Single(result.Commands));
    }
}
