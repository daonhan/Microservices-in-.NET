using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Tests.IntegrationEvents;

public class PaymentFailedFlowTests : IntegrationTestBase
{
    public PaymentFailedFlowTests(OrderWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task PaymentFailed_WhenOrderPending_CancelsAndEmitsOrderCancelled()
    {
        var order = new Service.Models.Order { CustomerId = "cust-1" };
        await OrderContext.CreateOrder(order);

        await DispatchAsync(new PaymentFailedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: order.OrderId,
            CustomerId: order.CustomerId,
            Reason: "Card declined by issuer"));

        OrderContext.ChangeTracker.Clear();
        var reloaded = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        Assert.Contains(outboxEvents, e =>
            e.EventType.Contains(nameof(OrderCancelledEvent), StringComparison.Ordinal) &&
            e.Data.Contains(order.OrderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PaymentFailed_WhenOrderAlreadyCancelled_IsNoOp()
    {
        var order = new Service.Models.Order { CustomerId = "cust-2" };
        order.TryCancel();
        await OrderContext.CreateOrder(order);

        await DispatchAsync(new PaymentFailedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: order.OrderId,
            CustomerId: order.CustomerId,
            Reason: "Order cancelled"));

        OrderContext.ChangeTracker.Clear();
        var reloaded = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task PaymentFailed_WhenOrderUnknown_IsNoOp()
    {
        await DispatchAsync(new PaymentFailedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: "cust-x",
            Reason: "Card declined"));
    }

    private async Task DispatchAsync(PaymentFailedEvent @event)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredKeyedService<IEventHandler>(typeof(PaymentFailedEvent));
        await handler.Handle(@event);
    }
}
