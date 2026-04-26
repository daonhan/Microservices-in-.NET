using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Tests.IntegrationEvents;

public class PaymentAuthorizedFlowTests : IntegrationTestBase
{
    public PaymentAuthorizedFlowTests(OrderWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task PaymentAuthorized_WhenOrderPending_ConfirmsAndEmitsOrderConfirmed()
    {
        var order = new Service.Models.Order { CustomerId = "cust-1" };
        await OrderContext.CreateOrder(order);

        await DispatchAsync(new PaymentAuthorizedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: order.OrderId,
            CustomerId: order.CustomerId,
            Amount: 50.00m,
            Currency: "USD"));

        OrderContext.ChangeTracker.Clear();
        var reloaded = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Confirmed, reloaded.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        Assert.Contains(outboxEvents, e =>
            e.EventType.Contains(nameof(OrderConfirmedEvent), StringComparison.Ordinal) &&
            e.Data.Contains(order.OrderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PaymentAuthorized_WhenOrderAlreadyCancelled_IsNoOp()
    {
        var order = new Service.Models.Order { CustomerId = "cust-2" };
        order.TryCancel();
        await OrderContext.CreateOrder(order);

        await DispatchAsync(new PaymentAuthorizedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: order.OrderId,
            CustomerId: order.CustomerId,
            Amount: 10.00m,
            Currency: "USD"));

        OrderContext.ChangeTracker.Clear();
        var reloaded = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task PaymentAuthorized_WhenOrderUnknown_IsNoOp()
    {
        await DispatchAsync(new PaymentAuthorizedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: "cust-x",
            Amount: 1m,
            Currency: "USD"));
    }

    private async Task DispatchAsync(PaymentAuthorizedEvent @event)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredKeyedService<IEventHandler>(typeof(PaymentAuthorizedEvent));
        await handler.Handle(@event);
    }
}
