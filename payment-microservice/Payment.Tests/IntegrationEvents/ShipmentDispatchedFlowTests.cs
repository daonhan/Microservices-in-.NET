using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;

namespace Payment.Tests.IntegrationEvents;

public class ShipmentDispatchedFlowTests : IntegrationTestBase
{
    public ShipmentDispatchedFlowTests(PaymentWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Given_AuthorizedPayment_When_ShipmentDispatched_Then_CapturesAndEmitsEvent()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-capture";

        await DispatchAsync(new OrderCreatedEvent(
            orderId,
            customerId,
            new List<OrderItem> { new("p-1", 1, 25.00m) },
            "USD"));

        await DispatchAsync(new StockReservedEvent(orderId, 25.00m, "USD"));

        await DispatchAsync(BuildShipmentDispatchedEvent(orderId, customerId));

        PaymentContext.ChangeTracker.Clear();
        var payment = await PaymentContext.Payments
            .SingleAsync(p => p.OrderId == orderId);

        Assert.Equal(PaymentStatus.Captured, payment.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var matching = outboxEvents.Where(e =>
            e.EventType.Contains(nameof(PaymentCapturedEvent), StringComparison.Ordinal) &&
            e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(matching);
    }

    [Fact]
    public async Task ShipmentDispatched_WhenRedelivered_DoesNotDoubleCapture()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-capture-idem";

        await DispatchAsync(new OrderCreatedEvent(
            orderId,
            customerId,
            new List<OrderItem> { new("p-1", 1, 30.00m) },
            "USD"));

        await DispatchAsync(new StockReservedEvent(orderId, 30.00m, "USD"));

        await DispatchAsync(BuildShipmentDispatchedEvent(orderId, customerId));
        await DispatchAsync(BuildShipmentDispatchedEvent(orderId, customerId));

        PaymentContext.ChangeTracker.Clear();
        var payment = await PaymentContext.Payments
            .SingleAsync(p => p.OrderId == orderId);

        Assert.Equal(PaymentStatus.Captured, payment.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var matching = outboxEvents.Where(e =>
            e.EventType.Contains(nameof(PaymentCapturedEvent), StringComparison.Ordinal) &&
            e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(matching);
    }

    [Fact]
    public async Task ShipmentDispatched_WhenNoPaymentExists_IsNoOp()
    {
        var orderId = Guid.NewGuid();

        await DispatchAsync(BuildShipmentDispatchedEvent(orderId, "cust-missing"));

        PaymentContext.ChangeTracker.Clear();
        var payment = await PaymentContext.Payments
            .FirstOrDefaultAsync(p => p.OrderId == orderId);

        Assert.Null(payment);
    }

    private static ShipmentDispatchedEvent BuildShipmentDispatchedEvent(Guid orderId, string customerId) =>
        new(
            ShipmentId: Guid.NewGuid(),
            OrderId: orderId,
            CustomerId: customerId,
            CarrierKey: "GROUND",
            TrackingNumber: "TRACK-1",
            QuotedPriceAmount: 5.00m,
            QuotedPriceCurrency: "USD",
            OccurredAt: DateTime.UtcNow);

    private async Task DispatchAsync<TEvent>(TEvent @event)
        where TEvent : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(TEvent));
        await handler.Handle(@event);
    }
}
