using System.Net.Http.Json;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Inventory.Service.ApiModels;
using Inventory.Service.IntegrationEvents;
using Inventory.Service.IntegrationEvents.EventHandlers;
using Inventory.Service.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Api;

public class ReleaseReservationsTests : IntegrationTestBase
{
    public ReleaseReservationsTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task ReleaseOnCancellation_WhenHeld_ReturnsReservedToAvailable()
    {
        const int productId = 601;
        var orderId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 4,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 4,
        });
        InventoryContext.StockReservations.Add(new StockReservation
        {
            OrderId = orderId,
            ProductId = productId,
            WarehouseId = 1,
            Quantity = 4,
            Status = ReservationStatus.Held,
            CreatedAt = DateTime.UtcNow,
        });
        await InventoryContext.SaveChangesAsync();

        Subscribe<StockReleasedEvent>();

        await DispatchCancelAsync(new OrderCancelledEvent(orderId, "c-1"));

        InventoryContext.ChangeTracker.Clear();

        var reservation = InventoryContext.StockReservations.Single(r => r.OrderId == orderId);
        Assert.Equal(ReservationStatus.Released, reservation.Status);

        var item = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(10, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(10, item.Available);

        Assert.Contains(InventoryContext.StockMovements,
            m => m.OrderId == orderId && m.Type == MovementType.Release);

        SpinWait.SpinUntil(
            () => ReceivedEvents.ToArray().OfType<StockReleasedEvent>().Any(e => e.OrderId == orderId),
            TimeSpan.FromSeconds(5));
        var published = Assert.Single(ReceivedEvents.ToArray().OfType<StockReleasedEvent>(), e => e.OrderId == orderId);
        Assert.Equal(orderId, published.OrderId);
        var itemPublished = Assert.Single(published.Items);
        Assert.Equal(productId, itemPublished.ProductId);
        Assert.Equal(1, itemPublished.WarehouseId);
        Assert.Equal(4, itemPublished.Quantity);
    }

    [Fact]
    public async Task Given_HeldReservation_When_OrderConfirmed_Then_CommitsAndPublishesStockCommitted()
    {
        const int productId = 605;
        var orderId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 4,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 4,
        });
        InventoryContext.StockReservations.Add(new StockReservation
        {
            OrderId = orderId,
            ProductId = productId,
            WarehouseId = 1,
            Quantity = 4,
            Status = ReservationStatus.Held,
            CreatedAt = DateTime.UtcNow,
        });
        await InventoryContext.SaveChangesAsync();

        Subscribe<StockCommittedEvent>();

        await DispatchConfirmAsync(new OrderConfirmedEvent(orderId, "c-1"));

        InventoryContext.ChangeTracker.Clear();

        var reservation = InventoryContext.StockReservations.Single(r => r.OrderId == orderId);
        Assert.Equal(ReservationStatus.Committed, reservation.Status);

        var item = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(6, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(6, item.Available);

        Assert.Contains(InventoryContext.StockMovements,
            m => m.OrderId == orderId && m.Type == MovementType.Commit);

        SpinWait.SpinUntil(
            () => ReceivedEvents.ToArray().OfType<StockCommittedEvent>().Any(e => e.OrderId == orderId),
            TimeSpan.FromSeconds(5));
        var published = Assert.Single(ReceivedEvents.ToArray().OfType<StockCommittedEvent>(), e => e.OrderId == orderId);
        Assert.Equal(orderId, published.OrderId);
        var itemPublished = Assert.Single(published.Items);
        Assert.Equal(productId, itemPublished.ProductId);
        Assert.Equal(1, itemPublished.WarehouseId);
        Assert.Equal(4, itemPublished.Quantity);
    }

    [Fact]
    public async Task Given_ReservedThroughApi_When_OrderConfirmed_Then_CommitsAndPublishesStockCommitted()
    {
        const int productId = 606;
        var orderId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        Subscribe<StockCommittedEvent>();

        var client = CreateAuthenticatedClient();
        var reserve = await client.PostAsJsonAsync(
            $"/{productId}/reserve", new ReserveRequest(orderId, 3));
        reserve.EnsureSuccessStatusCode();

        await DispatchConfirmAsync(new OrderConfirmedEvent(orderId, "c-1"));

        InventoryContext.ChangeTracker.Clear();

        var reservation = InventoryContext.StockReservations.Single(r => r.OrderId == orderId);
        Assert.Equal(ReservationStatus.Committed, reservation.Status);

        var item = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(7, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(7, item.Available);

        Assert.Contains(InventoryContext.StockMovements,
            m => m.OrderId == orderId && m.Type == MovementType.Commit);

        SpinWait.SpinUntil(
            () => ReceivedEvents.ToArray().OfType<StockCommittedEvent>().Any(e => e.OrderId == orderId),
            TimeSpan.FromSeconds(5));
        var published = Assert.Single(ReceivedEvents.ToArray().OfType<StockCommittedEvent>(), e => e.OrderId == orderId);
        Assert.Equal(orderId, published.OrderId);
        var itemPublished = Assert.Single(published.Items);
        Assert.Equal(productId, itemPublished.ProductId);
        Assert.Equal(1, itemPublished.WarehouseId);
        Assert.Equal(3, itemPublished.Quantity);
    }

    [Fact]
    public async Task ReleaseOnCancellation_WhenCommitted_RestoresOnHand()
    {
        const int productId = 602;
        var orderId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 6,
            TotalReserved = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 6,
            Reserved = 0,
        });
        InventoryContext.StockReservations.Add(new StockReservation
        {
            OrderId = orderId,
            ProductId = productId,
            WarehouseId = 1,
            Quantity = 4,
            Status = ReservationStatus.Committed,
            CreatedAt = DateTime.UtcNow,
        });
        await InventoryContext.SaveChangesAsync();

        await DispatchCancelAsync(new OrderCancelledEvent(orderId, "c-1"));

        InventoryContext.ChangeTracker.Clear();

        var item = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(10, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);

        var reservation = InventoryContext.StockReservations.Single(r => r.OrderId == orderId);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
    }

    [Fact]
    public async Task ReleaseOnCancellation_WhenReplayed_IsNoOp()
    {
        const int productId = 603;
        var orderId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 5,
            TotalReserved = 2,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 5,
            Reserved = 2,
        });
        InventoryContext.StockReservations.Add(new StockReservation
        {
            OrderId = orderId,
            ProductId = productId,
            WarehouseId = 1,
            Quantity = 2,
            Status = ReservationStatus.Held,
            CreatedAt = DateTime.UtcNow,
        });
        await InventoryContext.SaveChangesAsync();

        await DispatchCancelAsync(new OrderCancelledEvent(orderId, "c-1"));
        await DispatchCancelAsync(new OrderCancelledEvent(orderId, "c-1"));

        InventoryContext.ChangeTracker.Clear();
        var item = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(5, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);

        var releaseMovements = InventoryContext.StockMovements
            .Where(m => m.OrderId == orderId && m.Type == MovementType.Release)
            .ToList();
        Assert.Single(releaseMovements);
    }

    [Fact]
    public async Task ReleaseOnCancellation_WhenNoReservations_IsNoOp()
    {
        var orderId = Guid.NewGuid();

        await DispatchCancelAsync(new OrderCancelledEvent(orderId, "c-1"));

        InventoryContext.ChangeTracker.Clear();
        Assert.Empty(InventoryContext.StockReservations.Where(r => r.OrderId == orderId));
        Assert.Empty(InventoryContext.StockMovements.Where(m => m.OrderId == orderId));
    }

    [Fact]
    public async Task CommitAfterRelease_IsNoOpWithoutSideEffects()
    {
        const int productId = 604;
        var orderId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 8,
            TotalReserved = 3,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 8,
            Reserved = 3,
        });
        InventoryContext.StockReservations.Add(new StockReservation
        {
            OrderId = orderId,
            ProductId = productId,
            WarehouseId = 1,
            Quantity = 3,
            Status = ReservationStatus.Held,
            CreatedAt = DateTime.UtcNow,
        });
        await InventoryContext.SaveChangesAsync();

        await DispatchCancelAsync(new OrderCancelledEvent(orderId, "c-1"));
        await DispatchConfirmAsync(new OrderConfirmedEvent(orderId, "c-1"));

        InventoryContext.ChangeTracker.Clear();
        var item = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(8, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);

        var reservation = InventoryContext.StockReservations.Single(r => r.OrderId == orderId);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
    }

    private async Task DispatchCancelAsync(OrderCancelledEvent @event)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(OrderCancelledEvent));
        await handler.Handle(@event);
    }

    private async Task DispatchConfirmAsync(OrderConfirmedEvent @event)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(OrderConfirmedEvent));
        await handler.Handle(@event);
    }
}
