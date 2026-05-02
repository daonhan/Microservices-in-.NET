using Order.Service.Infrastructure.Data;
using Order.Service.IntegrationEvents.EventHandlers;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Tests.IntegrationEvents;

public class StockReservationFailedHandlerUnitTests
{
    private sealed class RecordingOrderStore : IOrderStore
    {
        public Service.Models.Order? Order { get; init; }
        public bool ExecuteAsyncCalled { get; private set; }

        public Task CreateOrder(Service.Models.Order order) => Task.CompletedTask;
        public Task<Service.Models.Order?> GetCustomerOrderById(string customerId, string orderId)
            => Task.FromResult<Service.Models.Order?>(null);
        public Task<Service.Models.Order?> GetOrderById(Guid orderId) => Task.FromResult(Order);

        public async Task ExecuteAsync(Func<Task> unitOfWork)
        {
            ExecuteAsyncCalled = true;
            await unitOfWork();
        }
    }

    [Fact]
    public async Task Handle_WhenOrderPending_CancelsAndRaisesDomainEvent()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        var store = new RecordingOrderStore { Order = order };
        var handler = new StockReservationFailedEventHandler(store);

        await handler.Handle(new StockReservationFailedEvent(
            order.OrderId,
            [new FailedItem(1, 5, 2)]));

        Assert.True(store.ExecuteAsyncCalled);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Single(order.DomainEvents.OfType<OrderCancelledDomainEvent>());
    }

    [Fact]
    public async Task Handle_WhenOrderAlreadyCancelled_DoesNotRaiseEvent()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        order.TryCancel();
        order.DequeueDomainEvents();

        var store = new RecordingOrderStore { Order = order };
        var handler = new StockReservationFailedEventHandler(store);

        await handler.Handle(new StockReservationFailedEvent(
            order.OrderId,
            [new FailedItem(1, 5, 2)]));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public async Task Handle_WhenOrderUnknown_DoesNothing()
    {
        var store = new RecordingOrderStore { Order = null };
        var handler = new StockReservationFailedEventHandler(store);

        await handler.Handle(new StockReservationFailedEvent(
            Guid.NewGuid(),
            [new FailedItem(1, 5, 2)]));

        Assert.True(store.ExecuteAsyncCalled);
    }
}
