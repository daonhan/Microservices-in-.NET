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
    public void Given_StockReserving_When_StockReserved_Then_TransitionsToStockReserved()
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
            OrderSagaStep.StockReserving,
            SagaStatus.Running);

        var result = OrderSagaStateMachine.Transition(state, stockReserved);

        Assert.True(result.Changed);
        Assert.Empty(result.Commands);
        Assert.Equal(OrderSagaStep.StockReserved, result.State.CurrentStep);
        Assert.Equal(nameof(StockReservedEvent), result.State.LastStepResult);
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
