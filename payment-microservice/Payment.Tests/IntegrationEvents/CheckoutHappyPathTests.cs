using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;

namespace Payment.Tests.IntegrationEvents;

public class CheckoutHappyPathTests : IntegrationTestBase
{
    public CheckoutHappyPathTests(PaymentWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Given_OrderCreated_Then_StockReserved_AuthorizesPaymentAndEmitsEvent()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-happy";

        await DispatchAsync(new OrderCreatedEvent(
            orderId,
            customerId,
            new List<OrderItem> { new("p-1", 1, 50.00m) },
            "USD"));

        await DispatchAsync(new StockReservedEvent(orderId, 50.00m, "USD"));

        PaymentContext.ChangeTracker.Clear();
        var payment = await PaymentContext.Payments
            .SingleAsync(p => p.OrderId == orderId);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(customerId, payment.CustomerId);
        Assert.Equal(50.00m, payment.Amount);
        Assert.Equal("USD", payment.Currency);
        Assert.False(string.IsNullOrEmpty(payment.ProviderReference));

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var matching = outboxEvents.Where(e =>
            e.EventType.Contains(nameof(PaymentAuthorizedEvent), StringComparison.Ordinal) &&
            e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(matching);
    }

    [Fact]
    public async Task StockReserved_WhenOrderCustomerUnknown_IsNoOp()
    {
        var orderId = Guid.NewGuid();

        await DispatchAsync(new StockReservedEvent(orderId, 25.00m, "USD"));

        PaymentContext.ChangeTracker.Clear();
        var payment = await PaymentContext.Payments
            .FirstOrDefaultAsync(p => p.OrderId == orderId);

        Assert.Null(payment);
    }

    [Fact]
    public async Task StockReserved_WhenRedelivered_DoesNotCreateSecondPayment()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-idem";

        await DispatchAsync(new OrderCreatedEvent(
            orderId,
            customerId,
            new List<OrderItem> { new("p-1", 1, 10.00m) },
            "USD"));

        await DispatchAsync(new StockReservedEvent(orderId, 10.00m, "USD"));
        await DispatchAsync(new StockReservedEvent(orderId, 10.00m, "USD"));

        PaymentContext.ChangeTracker.Clear();
        var payments = await PaymentContext.Payments
            .Where(p => p.OrderId == orderId)
            .ToListAsync();

        Assert.Single(payments);
    }

    private async Task DispatchAsync<TEvent>(TEvent @event)
        where TEvent : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(TEvent));
        await handler.Handle(@event);
    }
}
