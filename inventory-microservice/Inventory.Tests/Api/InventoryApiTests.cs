using System.Net;
using System.Net.Http.Json;
using Inventory.Service.ApiModels;
using Inventory.Service.Models;

namespace Inventory.Tests.Api;

public class InventoryApiTests : IntegrationTestBase
{
    public InventoryApiTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetStockItem_WhenNoStockItemExists_ThenReturnsNotFound()
    {
        // Act
        var response = await HttpClient.GetAsync("/999999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStockItem_WhenStockItemExists_ThenReturnsStockItemWithDefaultWarehouse()
    {
        // Arrange
        const int productId = 42;
        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 2,
            LowStockThreshold = 5,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 2,
        });
        await InventoryContext.SaveChangesAsync();

        // Act
        var response = await HttpClient.GetAsync($"/{productId}");

        // Assert
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GetStockItemResponse>();

        Assert.NotNull(body);
        Assert.Equal(productId, body.ProductId);
        Assert.Equal(10, body.TotalOnHand);
        Assert.Equal(2, body.TotalReserved);
        Assert.Equal(8, body.Available);
        Assert.Equal(5, body.Threshold);
        Assert.Single(body.PerWarehouse);
        Assert.Equal("DEFAULT", body.PerWarehouse[0].WarehouseCode);
    }

    [Fact]
    public async Task Health_WhenCalled_ThenReturnsOk()
    {
        // Act
        var response = await HttpClient.GetAsync("/health");

        // Assert
        response.EnsureSuccessStatusCode();
    }
}
