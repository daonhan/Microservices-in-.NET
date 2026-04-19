using Inventory.Service.ApiModels;
using Inventory.Service.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Service.Endpoints;

public static class InventoryApiEndpoints
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{productId:int}", async Task<IResult> (
            [FromServices] IInventoryStore inventoryStore,
            int productId) =>
        {
            var stockItem = await inventoryStore.GetStockItem(productId);

            if (stockItem is null)
            {
                return TypedResults.NotFound($"Stock item for product {productId} not found");
            }

            var stockLevels = await inventoryStore.GetStockLevels(productId);

            var perWarehouse = stockLevels
                .Select(level => new StockLevelDto(
                    level.WarehouseId,
                    level.Warehouse?.Code ?? string.Empty,
                    level.OnHand,
                    level.Reserved))
                .ToList();

            var response = new GetStockItemResponse(
                stockItem.ProductId,
                stockItem.TotalOnHand,
                stockItem.TotalReserved,
                stockItem.Available,
                stockItem.LowStockThreshold,
                perWarehouse);

            return TypedResults.Ok(response);
        });

        routeBuilder.MapGet("/health", () => TypedResults.Ok(new { status = "healthy" }));
    }
}
