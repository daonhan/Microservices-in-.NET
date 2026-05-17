using ECommerce.Shared.IntegrationEvents.Commands;
using Saga.Service.IntegrationEvents;
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
    public void Given_StockReserving_When_StockReservationFailed_Then_ParksSagaInFailed()
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
        Assert.Empty(result.Commands);
        Assert.Equal(OrderSagaStep.StockReserving, result.State.CurrentStep);
        Assert.Equal(SagaStatus.Failed, result.State.Status);
        Assert.Equal(nameof(StockReservationFailedEvent), result.State.LastStepResult);
    }
}
