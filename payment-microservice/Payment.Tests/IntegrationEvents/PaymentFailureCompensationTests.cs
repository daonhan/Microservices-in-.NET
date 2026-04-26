using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;

namespace Payment.Tests.IntegrationEvents;

public class PaymentFailureCompensationTests : IntegrationTestBase
{
    public PaymentFailureCompensationTests(PaymentWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Given_OrderCreated_Then_DecliningStockReserved_FailsAndEmitsEvent()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-decline";

        await DispatchAsync(new OrderCreatedEvent(
            orderId,
            customerId,
            new List<OrderItem> { new("p-1", 1, 10.99m) },
            "USD"));

        await DispatchAsync(new StockReservedEvent(orderId, 10.99m, "USD"));

        PaymentContext.ChangeTracker.Clear();
        var payment = await PaymentContext.Payments
            .SingleAsync(p => p.OrderId == orderId);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(customerId, payment.CustomerId);
        Assert.Equal(10.99m, payment.Amount);
        Assert.Null(payment.ProviderReference);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var matching = outboxEvents.Where(e =>
            e.EventType.Contains(nameof(PaymentFailedEvent), StringComparison.Ordinal) &&
            e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(matching);
    }

    [Fact]
    public async Task DecliningStockReserved_WhenRedelivered_DoesNotCreateSecondPayment()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-decline-idem";

        await DispatchAsync(new OrderCreatedEvent(
            orderId,
            customerId,
            new List<OrderItem> { new("p-1", 1, 5.99m) },
            "USD"));

        await DispatchAsync(new StockReservedEvent(orderId, 5.99m, "USD"));
        await DispatchAsync(new StockReservedEvent(orderId, 5.99m, "USD"));

        PaymentContext.ChangeTracker.Clear();
        var payments = await PaymentContext.Payments
            .Where(p => p.OrderId == orderId)
            .ToListAsync();

        Assert.Single(payments);
        Assert.Equal(PaymentStatus.Failed, payments[0].Status);
    }

    private async Task DispatchAsync<TEvent>(TEvent @event)
        where TEvent : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(TEvent));
        await handler.Handle(@event);
    }
}
