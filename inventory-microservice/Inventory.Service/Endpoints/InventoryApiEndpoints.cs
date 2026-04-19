using System.Transactions;
using ECommerce.Shared.Infrastructure.Outbox;
using Inventory.Service.ApiModels;
using Inventory.Service.Infrastructure.Data;
using Inventory.Service.IntegrationEvents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.Endpoints;

public static class InventoryApiEndpoints
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/", async Task<IResult> (
            [FromServices] IInventoryStore inventoryStore) =>
        {
            var items = await inventoryStore.ListStockItems();

            var response = items
                .Select(item => new StockItemSummaryDto(
                    item.ProductId,
                    item.TotalOnHand,
                    item.TotalReserved,
                    item.Available,
                    item.LowStockThreshold))
                .ToList();

            return TypedResults.Ok(response);
        }).RequireAuthorization();

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

        routeBuilder.MapGet("/{productId:int}/movements", async Task<IResult> (
            [FromServices] IInventoryStore inventoryStore,
            int productId) =>
        {
            var movements = await inventoryStore.GetMovements(productId);

            var response = movements
                .Select(m => new StockMovementDto(
                    m.Id,
                    m.ProductId,
                    m.WarehouseId,
                    m.Type.ToString(),
                    m.Quantity,
                    m.OccurredAt,
                    m.OrderId,
                    m.Reason))
                .ToList();

            return TypedResults.Ok(response);
        }).RequireAuthorization();

        routeBuilder.MapPost("/{productId:int}/restock", async Task<IResult> (
            [FromServices] IInventoryStore inventoryStore,
            [FromServices] IOutboxStore outboxStore,
            int productId,
            RestockRequest request) =>
        {
            if (request.Quantity <= 0)
            {
                return TypedResults.BadRequest("Quantity must be greater than zero.");
            }

            RestockResult? result = null;

            await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                result = await inventoryStore.Restock(productId, request.Quantity);

                if (result is null)
                {
                    return;
                }

                await outboxStore.AddOutboxEvent(new StockAdjustedEvent(
                    productId,
                    result.WarehouseId,
                    request.Quantity,
                    result.NewOnHand));

                scope.Complete();
            });

            if (result is null)
            {
                return TypedResults.NotFound($"Stock item for product {productId} not found");
            }

            return TypedResults.Ok(new RestockResponse(productId, result.WarehouseId, result.NewOnHand));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapGet("/health", () => TypedResults.Ok(new { status = "healthy" }));
    }
}
