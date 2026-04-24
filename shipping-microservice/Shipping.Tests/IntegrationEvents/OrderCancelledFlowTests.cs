using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;

namespace Shipping.Tests.IntegrationEvents;

public class OrderCancelledFlowTests : IntegrationTestBase
{
    public OrderCancelledFlowTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task OrderCancelled_WhenShipmentIsPending_CancelsAndEmitsEvents()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-cancel-1";

        await DispatchAsync(new OrderConfirmedEvent(orderId, customerId));
        await DispatchAsync(new StockCommittedEvent(orderId, new List<CommittedItem>
        {
            new(ProductId: 10, WarehouseId: 1, Quantity: 2),
        }));

        await DispatchAsync(new OrderCancelledEvent(orderId, customerId));

        var shipment = await ShippingContext.Shipments
            .AsNoTracking()
            .FirstAsync(s => s.OrderId == orderId);
        Assert.Equal(ShipmentStatus.Cancelled, shipment.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var events = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(events, e => e.EventType.Contains(nameof(ShipmentCancelledEvent), StringComparison.Ordinal));
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal)
            && e.Data.Contains($"\"ToStatus\":{(int)ShipmentStatus.Cancelled}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OrderCancelled_WhenShipmentAlreadyShipped_LeavesShipmentUntouched()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-cancel-2";

        await DispatchAsync(new OrderConfirmedEvent(orderId, customerId));
        await DispatchAsync(new StockCommittedEvent(orderId, new List<CommittedItem>
        {
            new(ProductId: 20, WarehouseId: 1, Quantity: 1),
        }));

        // Drive shipment to Shipped state directly via aggregate so it's past the cancellable window.
        var shipmentEntity = await ShippingContext.Shipments.FirstAsync(s => s.OrderId == orderId);
        var now = DateTime.UtcNow;
        Assert.True(shipmentEntity.TryPick(now, ShipmentStatusSource.Admin));
        Assert.True(shipmentEntity.TryPack(now, ShipmentStatusSource.Admin));
        Assert.True(shipmentEntity.TryDispatch(now, ShipmentStatusSource.Admin));
        await ShippingContext.SaveChangesAsync();

        await DispatchAsync(new OrderCancelledEvent(orderId, customerId));

        var shipmentAfter = await ShippingContext.Shipments
            .AsNoTracking()
            .FirstAsync(s => s.OrderId == orderId);
        Assert.Equal(ShipmentStatus.Shipped, shipmentAfter.Status);
    }

    [Fact]
    public async Task OrderCancelled_WhenNoShipmentExists_IsNoOp()
    {
        await DispatchAsync(new OrderCancelledEvent(Guid.NewGuid(), "cust-noop"));
    }

    private async Task DispatchAsync<TEvent>(TEvent @event)
        where TEvent : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(TEvent));
        await handler.Handle(@event);
    }
}
