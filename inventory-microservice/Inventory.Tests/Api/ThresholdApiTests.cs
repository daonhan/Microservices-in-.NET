using System.Net;
using System.Net.Http.Json;
using Inventory.Service.ApiModels;
using Inventory.Service.IntegrationEvents;
using Inventory.Service.Models;

namespace Inventory.Tests.Api;

public class ThresholdApiTests : IntegrationTestBase
{
    public ThresholdApiTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task SetThreshold_WhenUnauthenticated_ThenReturnsUnauthorized()
    {
        var response = await HttpClient.PutAsJsonAsync("/100/threshold", new SetThresholdRequest(5));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetThreshold_WhenNonAdmin_ThenReturnsForbidden()
    {
        var client = CreateAuthenticatedClient(role: "User");

        var response = await client.PutAsJsonAsync("/100/threshold", new SetThresholdRequest(5));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetThreshold_WhenStockItemMissing_ThenReturnsNotFound()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync("/777777/threshold", new SetThresholdRequest(5));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetThreshold_WhenNegative_ThenReturnsBadRequest()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync("/100/threshold", new SetThresholdRequest(-1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetThreshold_WhenPersistsValue_ThenReturnsUpdatedThreshold()
    {
        const int productId = 301;
        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 20,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 20,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/{productId}/threshold", new SetThresholdRequest(3));

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SetThresholdResponse>();
        Assert.NotNull(body);
        Assert.Equal(productId, body.ProductId);
        Assert.Equal(3, body.Threshold);

        InventoryContext.ChangeTracker.Clear();
        var reloaded = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(3, reloaded.LowStockThreshold);
    }

    [Fact]
    public async Task SetThreshold_WhenCrossingFromAbove_ThenPublishesLowStockEvent()
    {
        const int productId = 302;
        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 5,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 5,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        Subscribe<LowStockEvent>();

        var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/{productId}/threshold", new SetThresholdRequest(10));
        response.EnsureSuccessStatusCode();

        SpinWait.SpinUntil(() => ReceivedEvents.Count > 0, TimeSpan.FromSeconds(5));

        Assert.NotEmpty(ReceivedEvents);
        var published = Assert.IsType<LowStockEvent>(ReceivedEvents.First());
        Assert.Equal(productId, published.ProductId);
        Assert.Equal(1, published.WarehouseId);
        Assert.Equal(5, published.Available);
        Assert.Equal(10, published.Threshold);
    }

    [Fact]
    public async Task SetThreshold_WhenStayingBelow_ThenDoesNotRepublish()
    {
        const int productId = 303;
        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 2,
            TotalReserved = 0,
            LowStockThreshold = 5,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 2,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        Subscribe<LowStockEvent>();

        var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/{productId}/threshold", new SetThresholdRequest(8));
        response.EnsureSuccessStatusCode();

        Thread.Sleep(TimeSpan.FromSeconds(2));

        Assert.Empty(ReceivedEvents);
    }
}
