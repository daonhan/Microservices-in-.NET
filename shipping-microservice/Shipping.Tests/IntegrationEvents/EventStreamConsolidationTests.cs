using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.ApiModels;
using Shipping.Service.Carriers;
using Shipping.Service.Infrastructure.Data.EntityFramework;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;

namespace Shipping.Tests.IntegrationEvents;

/// <summary>
/// Phase 7: Event-stream consolidation audit.
///
/// Asserts the canonical "milestone + ShipmentStatusChangedEvent" pair is
/// emitted for every aggregate transition, and that the overall ordered event
/// stream for a shipment matches what downstream consumers rely on.
/// </summary>
public class EventStreamConsolidationTests : IntegrationTestBase
{
    private const string GroundSecret = "test-ground-secret";

    public EventStreamConsolidationTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task HappyPath_OrderConfirmedThroughDelivered_EmitsExpectedOrderedEventStream()
    {
        var orderId = Guid.NewGuid();
        var customerId = $"cust-happy-{Guid.NewGuid():N}";

        // 1) Order confirmation + stock commitment ⇒ shipment created in Pending.
        await DispatchEventAsync(new OrderConfirmedEvent(orderId, customerId));
        await DispatchEventAsync(new StockCommittedEvent(orderId, new List<CommittedItem>
        {
            new(ProductId: 100, WarehouseId: 1, Quantity: 1),
        }));

        var shipmentId = await LookupShipmentIdAsync(orderId);

        // 2) Pick → 3) Pack → 4) Dispatch via HTTP.
        var adminClient = CreateAuthenticatedClient();
        (await adminClient.PostAsync($"/{shipmentId}/pick", content: null)).EnsureSuccessStatusCode();
        (await adminClient.PostAsync($"/{shipmentId}/pack", content: null)).EnsureSuccessStatusCode();

        var dispatchResponse = await adminClient.PostAsJsonAsync(
            $"/{shipmentId}/dispatch",
            new DispatchShipmentRequest(
                CarrierKey: FakeGroundCarrierGateway.Key,
                ShippingAddress: new ShippingAddressDto(
                    Recipient: "Jane Doe",
                    Line1: "1 Main St",
                    Line2: null,
                    City: "Austin",
                    State: "TX",
                    PostalCode: "78701",
                    Country: "US"),
                OverrideQuote: null));
        dispatchResponse.EnsureSuccessStatusCode();
        var dispatchBody = await dispatchResponse.Content.ReadFromJsonAsync<DispatchShipmentResponse>();
        Assert.NotNull(dispatchBody);
        var tracking = dispatchBody!.TrackingNumber;

        // 5) Carrier webhook → InTransit.
        var webhookClient = Factory.CreateClient();
        webhookClient.DefaultRequestHeaders.Add("X-Carrier-Secret", GroundSecret);
        (await webhookClient.PostAsJsonAsync(
            $"/webhooks/carrier/{FakeGroundCarrierGateway.Key}",
            new { trackingNumber = tracking, statusCode = (int)CarrierStatusCode.InTransit }))
            .EnsureSuccessStatusCode();

        // 6) Deliver via admin endpoint (InTransit → Delivered).
        (await adminClient.PostAsync($"/{shipmentId}/deliver", content: null)).EnsureSuccessStatusCode();

        // Collect outbox events scoped to this shipment.
        var events = await GetOutboxEventsForShipmentAsync(shipmentId);

        // Exact ordered sequence of (FromStatus, ToStatus) transitions for this shipment.
        var orderedTransitions = ReadStatusTransitions(events);

        Assert.Equal(
            new (ShipmentStatus? From, ShipmentStatus To)[]
            {
                (null, ShipmentStatus.Pending),
                (ShipmentStatus.Pending, ShipmentStatus.Picked),
                (ShipmentStatus.Picked, ShipmentStatus.Packed),
                (ShipmentStatus.Packed, ShipmentStatus.Shipped),
                (ShipmentStatus.Shipped, ShipmentStatus.InTransit),
                (ShipmentStatus.InTransit, ShipmentStatus.Delivered),
            },
            orderedTransitions);

        // Milestone events — one of each expected kind for this shipment.
        AssertMilestoneExists(events, nameof(ShipmentCreatedEvent));
        AssertMilestoneExists(events, nameof(ShipmentDispatchedEvent));
        AssertMilestoneExists(events, nameof(ShipmentDeliveredEvent));
    }

    [Fact]
    public async Task CancelledAfterOrderConfirmed_EmitsExpectedOrderedEventStream()
    {
        var orderId = Guid.NewGuid();
        var customerId = $"cust-cancel-{Guid.NewGuid():N}";

        await DispatchEventAsync(new OrderConfirmedEvent(orderId, customerId));
        await DispatchEventAsync(new StockCommittedEvent(orderId, new List<CommittedItem>
        {
            new(ProductId: 200, WarehouseId: 1, Quantity: 1),
        }));

        var shipmentId = await LookupShipmentIdAsync(orderId);

        await DispatchEventAsync(new OrderCancelledEvent(orderId, customerId));

        var events = await GetOutboxEventsForShipmentAsync(shipmentId);

        var orderedTransitions = ReadStatusTransitions(events);

        Assert.Equal(
            new (ShipmentStatus? From, ShipmentStatus To)[]
            {
                (null, ShipmentStatus.Pending),
                (ShipmentStatus.Pending, ShipmentStatus.Cancelled),
            },
            orderedTransitions);

        AssertMilestoneExists(events, nameof(ShipmentCreatedEvent));
        AssertMilestoneExists(events, nameof(ShipmentCancelledEvent));
        Assert.DoesNotContain(events, e => e.EventType.Contains(nameof(ShipmentDispatchedEvent), StringComparison.Ordinal));
        Assert.DoesNotContain(events, e => e.EventType.Contains(nameof(ShipmentDeliveredEvent), StringComparison.Ordinal));
        Assert.DoesNotContain(events, e => e.EventType.Contains(nameof(ShipmentFailedEvent), StringComparison.Ordinal));
        Assert.DoesNotContain(events, e => e.EventType.Contains(nameof(ShipmentReturnedEvent), StringComparison.Ordinal));
    }

    private async Task<Guid> LookupShipmentIdAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingContext>();
        var shipment = await context.Shipments
            .AsNoTracking()
            .FirstAsync(s => s.OrderId == orderId);
        return shipment.Id;
    }

    private async Task<List<OutboxEvent>> GetOutboxEventsForShipmentAsync(Guid shipmentId)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var all = await outboxStore.GetUnpublishedOutboxEvents();
        var needle = shipmentId.ToString();
        return all
            .Where(e => e.Data.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<(ShipmentStatus? From, ShipmentStatus To)> ReadStatusTransitions(
        IEnumerable<OutboxEvent> events)
    {
        // Order by OccurredAt from the payload rather than the outbox row's
        // CreatedDate — sibling events in the same transaction can share a
        // millisecond timestamp on CreatedDate, but OccurredAt is advanced by
        // the test via separate HTTP calls / event dispatches.
        return events
            .Where(e => e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal))
            .Select(e => ParseStatusChanged(e.Data))
            .OrderBy(t => t.OccurredAt)
            .Select(t => (t.From, t.To))
            .ToList();
    }

    private static (ShipmentStatus? From, ShipmentStatus To, DateTime OccurredAt) ParseStatusChanged(string data)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        ShipmentStatus? from = null;
        if (root.TryGetProperty("FromStatus", out var fromElement)
            && fromElement.ValueKind == JsonValueKind.Number)
        {
            from = (ShipmentStatus)fromElement.GetInt32();
        }

        var to = (ShipmentStatus)root.GetProperty("ToStatus").GetInt32();
        var occurredAt = root.GetProperty("OccurredAt").GetDateTime();
        return (from, to, occurredAt);
    }

    private static void AssertMilestoneExists(IReadOnlyCollection<OutboxEvent> events, string eventTypeName)
    {
        Assert.Contains(events, e => e.EventType.Contains(eventTypeName, StringComparison.Ordinal));
    }

    private async Task DispatchEventAsync<TEvent>(TEvent @event)
        where TEvent : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(TEvent));
        await handler.Handle(@event);
    }
}
