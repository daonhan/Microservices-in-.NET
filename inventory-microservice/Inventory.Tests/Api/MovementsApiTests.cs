using System.Net;
using System.Net.Http.Json;
using Inventory.Service.ApiModels;
using Inventory.Service.Models;

namespace Inventory.Tests.Api;

public class MovementsApiTests : IntegrationTestBase
{
    public MovementsApiTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetMovements_WhenUnauthenticated_ThenReturnsUnauthorized()
    {
        // Act
        var response = await HttpClient.GetAsync("/100/movements");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMovements_WhenExists_ThenReturnsChronologicalList()
    {
        // Arrange
        const int productId = 401;
        var now = DateTime.UtcNow;

        InventoryContext.StockMovements.Add(new StockMovement
        {
            ProductId = productId,
            WarehouseId = 1,
            Type = MovementType.Restock,
            Quantity = 5,
            OccurredAt = now.AddMinutes(-10),
        });
        InventoryContext.StockMovements.Add(new StockMovement
        {
            ProductId = productId,
            WarehouseId = 1,
            Type = MovementType.Reserve,
            Quantity = 2,
            OccurredAt = now.AddMinutes(-5),
        });
        await InventoryContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync($"/{productId}/movements");

        // Assert
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<List<StockMovementDto>>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Count);
        Assert.Equal("Restock", body[0].Type);
        Assert.Equal("Reserve", body[1].Type);
    }
}
