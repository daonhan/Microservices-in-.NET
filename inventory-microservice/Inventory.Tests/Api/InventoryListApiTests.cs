using System.Net;
using System.Net.Http.Json;
using Inventory.Service.ApiModels;
using Inventory.Service.Models;

namespace Inventory.Tests.Api;

public class InventoryListApiTests : IntegrationTestBase
{
    public InventoryListApiTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetInventory_WhenUnauthenticated_ThenReturnsUnauthorized()
    {
        // Act
        var response = await HttpClient.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetInventory_WhenAuthenticated_ThenReturnsAllStockItems()
    {
        // Arrange
        InventoryContext.StockItems.Add(new StockItem { ProductId = 301, TotalOnHand = 4, TotalReserved = 1, LowStockThreshold = 2 });
        InventoryContext.StockItems.Add(new StockItem { ProductId = 302, TotalOnHand = 0, TotalReserved = 0, LowStockThreshold = 0 });
        await InventoryContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<List<StockItemSummaryDto>>();
        Assert.NotNull(body);
        Assert.Contains(body, s => s.ProductId == 301 && s.Available == 3);
        Assert.Contains(body, s => s.ProductId == 302 && s.Available == 0);
    }
}
