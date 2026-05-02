using Order.Service.Infrastructure.Data;
using Order.Service.IntegrationEvents.EventHandlers;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Tests.IntegrationEvents;

public class PaymentAuthorizedHandlerUnitTests
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
    public async Task Handle_WhenOrderPending_ConfirmsAndRaisesDomainEvent()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        var store = new RecordingOrderStore { Order = order };
        var handler = new PaymentAuthorizedEventHandler(store);

        await handler.Handle(new PaymentAuthorizedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: order.OrderId,
            CustomerId: order.CustomerId,
            Amount: 10m,
            Currency: "USD"));

        Assert.True(store.ExecuteAsyncCalled);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Single(order.DomainEvents.OfType<OrderConfirmedDomainEvent>());
    }

    [Fact]
    public async Task Handle_WhenOrderUnknown_DoesNothing()
    {
        var store = new RecordingOrderStore { Order = null };
        var handler = new PaymentAuthorizedEventHandler(store);

        await handler.Handle(new PaymentAuthorizedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: "c-x",
            Amount: 1m,
            Currency: "USD"));

        Assert.True(store.ExecuteAsyncCalled);
    }

    [Fact]
    public async Task Handle_WhenOrderAlreadyConfirmed_DoesNotRaiseEvent()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        order.TryConfirm();
        order.DequeueDomainEvents();

        var store = new RecordingOrderStore { Order = order };
        var handler = new PaymentAuthorizedEventHandler(store);

        await handler.Handle(new PaymentAuthorizedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: order.OrderId,
            CustomerId: order.CustomerId,
            Amount: 5m,
            Currency: "USD"));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Empty(order.DomainEvents);
    }
}
