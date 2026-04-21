using System.Transactions;
using ECommerce.Shared.Infrastructure.Outbox;
using Inventory.Service.ApiModels;
using Inventory.Service.Infrastructure.Data;
using Inventory.Service.IntegrationEvents;
using Inventory.Service.Models;
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

                var lowStock = StockLevelMonitor.TryLowStockCrossing(
                    productId,
                    result.WarehouseId,
                    result.AvailableBefore,
                    result.AvailableAfter,
                    result.Threshold,
                    result.Threshold);
                if (lowStock is not null)
                {
                    await outboxStore.AddOutboxEvent(lowStock);
                }

                var depleted = StockLevelMonitor.TryDepletedCrossing(
                    productId,
                    result.WarehouseId,
                    result.AvailableBefore,
                    result.AvailableAfter);
                if (depleted is not null)
                {
                    await outboxStore.AddOutboxEvent(depleted);
                }

                scope.Complete();
            });

            if (result is null)
            {
                return TypedResults.NotFound($"Stock item for product {productId} not found");
            }

            return TypedResults.Ok(new RestockResponse(productId, result.WarehouseId, result.NewOnHand));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPut("/{productId:int}/threshold", async Task<IResult> (
            [FromServices] IInventoryStore inventoryStore,
            [FromServices] IOutboxStore outboxStore,
            int productId,
            SetThresholdRequest request) =>
        {
            if (request.Threshold < 0)
            {
                return TypedResults.BadRequest("Threshold must be zero or greater.");
            }

            SetThresholdResult? result = null;

            await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                result = await inventoryStore.SetThreshold(productId, request.Threshold);

                if (result is null)
                {
                    return;
                }

                var lowStock = StockLevelMonitor.TryLowStockCrossing(
                    productId,
                    result.WarehouseId,
                    result.Available,
                    result.Available,
                    result.ThresholdBefore,
                    result.ThresholdAfter);
                if (lowStock is not null)
                {
                    await outboxStore.AddOutboxEvent(lowStock);
                }

                scope.Complete();
            });

            if (result is null)
            {
                return TypedResults.NotFound($"Stock item for product {productId} not found");
            }

            return TypedResults.Ok(new SetThresholdResponse(productId, result.ThresholdAfter));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/{productId:int}/reserve", async Task<IResult> (
            [FromServices] IInventoryStore inventoryStore,
            [FromServices] IOutboxStore outboxStore,
            int productId,
            ReserveRequest request) =>
        {
            if (request.Quantity <= 0)
            {
                return TypedResults.BadRequest("Quantity must be greater than zero.");
            }

            if (request.OrderId == Guid.Empty)
            {
                return TypedResults.BadRequest("OrderId is required.");
            }

            ReserveResult? outcome = null;

            await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                outcome = await inventoryStore.Reserve(
                    request.OrderId,
                    [new ReserveLine(productId, request.Quantity)]);

                if (outcome.Reserved && !outcome.AlreadyProcessed)
                {
                    var published = outcome.Lines
                        .Select(l => new ReservedItem(l.ProductId, l.WarehouseId, l.Quantity))
                        .ToList();

                    await outboxStore.AddOutboxEvent(new StockReservedEvent(request.OrderId, published));
                }

                scope.Complete();
            });

            if (outcome is null || !outcome.Reserved)
            {
                return TypedResults.Conflict("Insufficient stock or unknown product.");
            }

            var lines = outcome.Lines
                .Select(l => new ReservedLineDto(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            return TypedResults.Ok(new ReserveResponse(request.OrderId, lines));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/{productId:int}/backorder", async Task<IResult> (
            [FromServices] IInventoryStore inventoryStore,
            int productId,
            BackorderRequestDto request) =>
        {
            if (request.Quantity <= 0)
            {
                return TypedResults.BadRequest("Quantity must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(request.CustomerId))
            {
                return TypedResults.BadRequest("CustomerId is required.");
            }

            var result = await inventoryStore.CreateBackorder(request.CustomerId, productId, request.Quantity);

            if (result is null)
            {
                return TypedResults.NotFound($"Stock item for product {productId} not found");
            }

            return TypedResults.Ok(new BackorderResponse(
                result.Id,
                result.CustomerId,
                result.ProductId,
                result.Quantity,
                result.CreatedAt));
        }).RequireAuthorization();

        routeBuilder.MapGet("/health", () => TypedResults.Ok(new { status = "healthy" }));
    }
}
