using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;

namespace Shipping.Tests.IntegrationEvents;

public class ShipmentCreationFlowTests : IntegrationTestBase
{
    public ShipmentCreationFlowTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Given_OrderConfirmed_Then_StockCommitted_ThenCreatesOneShipmentPerWarehouse()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-42";

        await DispatchAsync(new OrderConfirmedEvent(orderId, customerId));

        var stockEvent = new StockCommittedEvent(orderId, new List<CommittedItem>
        {
            new(ProductId: 10, WarehouseId: 1, Quantity: 3),
            new(ProductId: 11, WarehouseId: 1, Quantity: 1),
            new(ProductId: 12, WarehouseId: 2, Quantity: 5),
        });
        await DispatchAsync(stockEvent);

        var shipments = await ShippingContext.Shipments
            .Include(s => s.Lines)
            .Where(s => s.OrderId == orderId)
            .OrderBy(s => s.WarehouseId)
            .ToListAsync();

        Assert.Equal(2, shipments.Count);
        Assert.All(shipments, s => Assert.Equal(ShipmentStatus.Pending, s.Status));
        Assert.All(shipments, s => Assert.Equal(customerId, s.CustomerId));
        Assert.Equal(1, shipments[0].WarehouseId);
        Assert.Equal(2, shipments[0].Lines.Count);
        Assert.Equal(2, shipments[1].WarehouseId);
        Assert.Single(shipments[1].Lines);

        using var outboxScope = Factory.Services.CreateScope();
        var outboxStore = outboxScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        Assert.Contains(outboxEvents, e => e.EventType.Contains(nameof(ShipmentCreatedEvent), StringComparison.Ordinal));
        Assert.Contains(outboxEvents, e => e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal));
        Assert.Equal(2, outboxEvents.Count(e => e.EventType.Contains(nameof(ShipmentCreatedEvent), StringComparison.Ordinal)));
        Assert.Equal(2, outboxEvents.Count(e => e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal)));
    }

    private async Task DispatchAsync<TEvent>(TEvent @event)
        where TEvent : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(TEvent));
        await handler.Handle(@event);
    }
}
