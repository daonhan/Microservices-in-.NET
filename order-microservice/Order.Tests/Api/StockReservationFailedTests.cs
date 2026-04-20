using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Tests.Api;

public class StockReservationFailedTests : IntegrationTestBase
{
    public StockReservationFailedTests(OrderWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task ReservationFailed_WhenOrderIsPending_ThenTransitionsToCancelled()
    {
        var order = new Service.Models.Order { CustomerId = "cx-1" };
        await OrderContext.CreateOrder(order);

        await DispatchAsync(new StockReservationFailedEvent(order.OrderId,
            [new FailedItem(1, 5, 2)]));

        OrderContext.ChangeTracker.Clear();
        var reloaded = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task ReservationFailed_WhenOrderAlreadyCancelled_IsNoOp()
    {
        var order = new Service.Models.Order { CustomerId = "cx-2" };
        order.TryCancel();
        await OrderContext.CreateOrder(order);

        await DispatchAsync(new StockReservationFailedEvent(order.OrderId,
            [new FailedItem(1, 3, 0)]));

        OrderContext.ChangeTracker.Clear();
        var reloaded = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task ReservationFailed_WhenOrderUnknown_IsNoOp()
    {
        await DispatchAsync(new StockReservationFailedEvent(Guid.NewGuid(),
            [new FailedItem(1, 3, 0)]));
    }

    private async Task DispatchAsync(StockReservationFailedEvent @event)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredKeyedService<IEventHandler>(typeof(StockReservationFailedEvent));
        await handler.Handle(@event);
    }
}
