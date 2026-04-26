using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;

namespace Payment.Tests.IntegrationEvents;

public class PaymentOrderCancelledTests : IntegrationTestBase
{
    public PaymentOrderCancelledTests(PaymentWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task OrderCancelled_WhenAuthorizedRowExists_VoidsAndEmitsPaymentFailed()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-cancel-auth";

        await DispatchAsync(new OrderCreatedEvent(
            orderId,
            customerId,
            new List<OrderItem> { new("p-1", 1, 50.00m) },
            "USD"));

        await DispatchAsync(new StockReservedEvent(orderId, 50.00m, "USD"));

        await DispatchAsync(new OrderCancelledEvent(orderId, customerId));

        PaymentContext.ChangeTracker.Clear();
        var payment = await PaymentContext.Payments.SingleAsync(p => p.OrderId == orderId);

        Assert.Equal(PaymentStatus.Failed, payment.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var failedEvents = outboxEvents.Where(e =>
            e.EventType.Contains(nameof(PaymentFailedEvent), StringComparison.Ordinal) &&
            e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(failedEvents);
        Assert.Contains("Order cancelled", failedEvents[0].Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderCancelled_WhenNoPaymentRow_IsNoOp()
    {
        var orderId = Guid.NewGuid();

        await DispatchAsync(new OrderCancelledEvent(orderId, "cust-x"));

        PaymentContext.ChangeTracker.Clear();
        var payment = await PaymentContext.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);

        Assert.Null(payment);
    }

    [Fact]
    public async Task OrderCancelled_WhenAlreadyFailed_IsNoOp()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-already-failed";

        await DispatchAsync(new OrderCreatedEvent(
            orderId,
            customerId,
            new List<OrderItem> { new("p-1", 1, 10.99m) },
            "USD"));

        await DispatchAsync(new StockReservedEvent(orderId, 10.99m, "USD"));

        // Snapshot UpdatedAt so we can confirm cancel handler does not touch the row.
        PaymentContext.ChangeTracker.Clear();
        var before = await PaymentContext.Payments.SingleAsync(p => p.OrderId == orderId);
        var beforeUpdatedAt = before.UpdatedAt;

        await DispatchAsync(new OrderCancelledEvent(orderId, customerId));

        PaymentContext.ChangeTracker.Clear();
        var after = await PaymentContext.Payments.SingleAsync(p => p.OrderId == orderId);

        Assert.Equal(PaymentStatus.Failed, after.Status);
        Assert.Equal(beforeUpdatedAt, after.UpdatedAt);
    }

    [Fact]
    public async Task OrderCancelled_Redelivered_AfterVoid_IsNoOp()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-cancel-redeliver";

        await DispatchAsync(new OrderCreatedEvent(
            orderId,
            customerId,
            new List<OrderItem> { new("p-1", 1, 30.00m) },
            "USD"));

        await DispatchAsync(new StockReservedEvent(orderId, 30.00m, "USD"));
        await DispatchAsync(new OrderCancelledEvent(orderId, customerId));

        PaymentContext.ChangeTracker.Clear();
        var firstUpdate = (await PaymentContext.Payments.SingleAsync(p => p.OrderId == orderId)).UpdatedAt;

        await DispatchAsync(new OrderCancelledEvent(orderId, customerId));

        PaymentContext.ChangeTracker.Clear();
        var after = await PaymentContext.Payments.SingleAsync(p => p.OrderId == orderId);

        Assert.Equal(PaymentStatus.Failed, after.Status);
        Assert.Equal(firstUpdate, after.UpdatedAt);
    }

    private async Task DispatchAsync<TEvent>(TEvent @event)
        where TEvent : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(TEvent));
        await handler.Handle(@event);
    }
}
