using System.Net;
using System.Net.Http.Json;
using Inventory.Service.ApiModels;
using Inventory.Service.IntegrationEvents;
using Inventory.Service.Models;

namespace Inventory.Tests.Api;

public class RestockApiTests : IntegrationTestBase
{
    public RestockApiTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Restock_WhenUnauthenticated_ThenReturnsUnauthorized()
    {
        // Act
        var response = await HttpClient.PostAsJsonAsync("/100/restock", new RestockRequest(5));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Restock_WhenNonAdmin_ThenReturnsForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(role: "User");

        // Act
        var response = await client.PostAsJsonAsync("/100/restock", new RestockRequest(5));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Restock_WhenStockItemMissing_ThenReturnsNotFound()
    {
        // Arrange
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/888888/restock", new RestockRequest(5));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Restock_WithInvalidQuantity_ThenReturnsBadRequest()
    {
        // Arrange
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/100/restock", new RestockRequest(0));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Restock_WhenSuccessful_ThenIncrementsOnHandAppendsMovementAndPublishesEvent()
    {
        // Arrange
        const int productId = 201;
        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 0,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 0,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        Subscribe<StockAdjustedEvent>();

        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync($"/{productId}/restock", new RestockRequest(7));

        // Assert
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RestockResponse>();
        Assert.NotNull(body);
        Assert.Equal(productId, body.ProductId);
        Assert.Equal(1, body.WarehouseId);
        Assert.Equal(7, body.NewOnHand);

        InventoryContext.ChangeTracker.Clear();
        var reloadedItem = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(7, reloadedItem.TotalOnHand);

        var movements = InventoryContext.StockMovements.Where(m => m.ProductId == productId).ToList();
        Assert.Single(movements);
        Assert.Equal(MovementType.Restock, movements[0].Type);
        Assert.Equal(7, movements[0].Quantity);

        SpinWait.SpinUntil(() => ReceivedEvents.Count > 0, TimeSpan.FromSeconds(5));

        Assert.NotEmpty(ReceivedEvents);
        var published = Assert.IsType<StockAdjustedEvent>(ReceivedEvents.First());
        Assert.Equal(productId, published.ProductId);
        Assert.Equal(1, published.WarehouseId);
        Assert.Equal(7, published.Quantity);
        Assert.Equal(7, published.NewOnHand);
    }
}
