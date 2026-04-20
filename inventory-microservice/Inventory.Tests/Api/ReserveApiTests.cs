using System.Net;
using System.Net.Http.Json;
using Inventory.Service.ApiModels;
using Inventory.Service.IntegrationEvents;
using Inventory.Service.Models;

namespace Inventory.Tests.Api;

public class ReserveApiTests : IntegrationTestBase
{
    public ReserveApiTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Reserve_WhenUnauthenticated_ThenReturnsUnauthorized()
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/100/reserve", new ReserveRequest(Guid.NewGuid(), 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reserve_WhenNonAdmin_ThenReturnsForbidden()
    {
        var client = CreateAuthenticatedClient(role: "User");

        var response = await client.PostAsJsonAsync(
            "/100/reserve", new ReserveRequest(Guid.NewGuid(), 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reserve_WithInvalidQuantity_ThenReturnsBadRequest()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/100/reserve", new ReserveRequest(Guid.NewGuid(), 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reserve_WithEmptyOrderId_ThenReturnsBadRequest()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/100/reserve", new ReserveRequest(Guid.Empty, 2));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reserve_WhenInsufficientStock_ThenReturnsConflict()
    {
        const int productId = 301;
        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 2,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 2,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/{productId}/reserve", new ReserveRequest(Guid.NewGuid(), 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reserve_WhenSuccessful_ThenPersistsReservationAndPublishesEvent()
    {
        const int productId = 302;
        var orderId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        Subscribe<StockReservedEvent>();

        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/{productId}/reserve", new ReserveRequest(orderId, 3));

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ReserveResponse>();
        Assert.NotNull(body);
        Assert.Equal(orderId, body.OrderId);
        Assert.Single(body.Lines);
        Assert.Equal(productId, body.Lines[0].ProductId);
        Assert.Equal(3, body.Lines[0].Quantity);

        InventoryContext.ChangeTracker.Clear();
        var reloadedItem = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(10, reloadedItem.TotalOnHand);
        Assert.Equal(3, reloadedItem.TotalReserved);
        Assert.Equal(7, reloadedItem.Available);

        var reservation = InventoryContext.StockReservations.Single(r => r.OrderId == orderId);
        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Equal(3, reservation.Quantity);

        var movement = InventoryContext.StockMovements.Single(m => m.OrderId == orderId);
        Assert.Equal(MovementType.Reserve, movement.Type);

        SpinWait.SpinUntil(() => ReceivedEvents.Count > 0, TimeSpan.FromSeconds(5));
        Assert.NotEmpty(ReceivedEvents);
        var published = Assert.IsType<StockReservedEvent>(ReceivedEvents.First());
        Assert.Equal(orderId, published.OrderId);
    }

    [Fact]
    public async Task Reserve_WhenReplayed_ThenIsIdempotent()
    {
        const int productId = 303;
        var orderId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync(
            $"/{productId}/reserve", new ReserveRequest(orderId, 3));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/{productId}/reserve", new ReserveRequest(orderId, 3));
        second.EnsureSuccessStatusCode();

        InventoryContext.ChangeTracker.Clear();
        var reservations = InventoryContext.StockReservations
            .Where(r => r.OrderId == orderId).ToList();
        Assert.Single(reservations);

        var reloadedItem = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(3, reloadedItem.TotalReserved);
    }
}
