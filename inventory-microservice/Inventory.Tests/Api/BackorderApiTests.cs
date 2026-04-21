using System.Net;
using System.Net.Http.Json;
using Inventory.Service.ApiModels;
using Inventory.Service.Models;

namespace Inventory.Tests.Api;

public class BackorderApiTests : IntegrationTestBase
{
    public BackorderApiTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Backorder_WhenUnauthenticated_ThenReturnsUnauthorized()
    {
        // Act
        var response = await HttpClient.PostAsJsonAsync(
            "/400/backorder",
            new BackorderRequestDto("customer-1", 3));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Backorder_WithInvalidQuantity_ThenReturnsBadRequest()
    {
        // Arrange
        var client = CreateAuthenticatedClient(role: "User");

        // Act
        var response = await client.PostAsJsonAsync(
            "/400/backorder",
            new BackorderRequestDto("customer-1", 0));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Backorder_WithoutCustomerId_ThenReturnsBadRequest()
    {
        // Arrange
        var client = CreateAuthenticatedClient(role: "User");

        // Act
        var response = await client.PostAsJsonAsync(
            "/400/backorder",
            new BackorderRequestDto("", 2));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Backorder_WhenStockItemMissing_ThenReturnsNotFound()
    {
        // Arrange
        var client = CreateAuthenticatedClient(role: "User");

        // Act
        var response = await client.PostAsJsonAsync(
            "/987654/backorder",
            new BackorderRequestDto("customer-1", 3));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Backorder_WhenAuthenticated_ThenPersistsRequest()
    {
        // Arrange
        const int productId = 401;
        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 0,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        await InventoryContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient(role: "User");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/{productId}/backorder",
            new BackorderRequestDto("customer-alice", 5));

        // Assert
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<BackorderResponse>();
        Assert.NotNull(body);
        Assert.Equal("customer-alice", body.CustomerId);
        Assert.Equal(productId, body.ProductId);
        Assert.Equal(5, body.Quantity);

        InventoryContext.ChangeTracker.Clear();
        var stored = InventoryContext.BackorderRequests
            .Single(b => b.Id == body.Id);
        Assert.Equal("customer-alice", stored.CustomerId);
        Assert.Equal(5, stored.Quantity);
        Assert.Null(stored.FulfilledAt);
    }

    [Fact]
    public async Task Restock_WhenBackordersPending_ThenFulfillsInFifoOrder()
    {
        // Arrange
        const int productId = 402;
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

        var earliest = new BackorderRequest
        {
            CustomerId = "customer-first",
            ProductId = productId,
            Quantity = 3,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        var later = new BackorderRequest
        {
            CustomerId = "customer-second",
            ProductId = productId,
            Quantity = 4,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        InventoryContext.BackorderRequests.Add(earliest);
        InventoryContext.BackorderRequests.Add(later);
        await InventoryContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient();

        // Act: restock enough to cover both (3 + 4 = 7)
        var response = await client.PostAsJsonAsync($"/{productId}/restock", new RestockRequest(7));

        // Assert
        response.EnsureSuccessStatusCode();

        InventoryContext.ChangeTracker.Clear();
        var reloaded = InventoryContext.BackorderRequests
            .Where(b => b.ProductId == productId)
            .OrderBy(b => b.CreatedAt)
            .ToList();

        Assert.NotNull(reloaded[0].FulfilledAt);
        Assert.NotNull(reloaded[1].FulfilledAt);
    }

    [Fact]
    public async Task Restock_WhenPartialCoverage_ThenFulfillsOnlyLeadingBackorders()
    {
        // Arrange
        const int productId = 403;
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

        var first = new BackorderRequest
        {
            CustomerId = "cust-1",
            ProductId = productId,
            Quantity = 3,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        var second = new BackorderRequest
        {
            CustomerId = "cust-2",
            ProductId = productId,
            Quantity = 5,
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
        };
        var third = new BackorderRequest
        {
            CustomerId = "cust-3",
            ProductId = productId,
            Quantity = 2,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        InventoryContext.BackorderRequests.Add(first);
        InventoryContext.BackorderRequests.Add(second);
        InventoryContext.BackorderRequests.Add(third);
        await InventoryContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient();

        // Act: restock only 4 units — only the first (3) fits; the second (5) doesn't.
        // Third (2) should NOT jump ahead of the unfulfilled second (FIFO halts at first miss).
        var response = await client.PostAsJsonAsync($"/{productId}/restock", new RestockRequest(4));

        // Assert
        response.EnsureSuccessStatusCode();

        InventoryContext.ChangeTracker.Clear();
        var reloaded = InventoryContext.BackorderRequests
            .Where(b => b.ProductId == productId)
            .OrderBy(b => b.CreatedAt)
            .ToList();

        Assert.NotNull(reloaded[0].FulfilledAt);
        Assert.Null(reloaded[1].FulfilledAt);
        Assert.Null(reloaded[2].FulfilledAt);
    }
}
