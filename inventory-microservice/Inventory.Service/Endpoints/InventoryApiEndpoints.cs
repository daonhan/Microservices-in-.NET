using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Inventory.Service.ApiModels;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Domain.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Service.Endpoints;

public static class InventoryApiEndpoints
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{productId:int}/reserve", async Task<IResult> (
            [FromServices] IInventoryStore inventoryStore,
            [FromServices] IOutboxUnitOfWork outboxUnitOfWork,
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

            await outboxUnitOfWork.ExecuteAsync(async () =>
            {
                outcome = await inventoryStore.Reserve(
                    request.OrderId,
                    [new ReserveLine(productId, request.Quantity)]);

                if (!outcome.Reserved || outcome.AlreadyProcessed)
                {
                    return [];
                }

                var published = outcome.Lines
                    .Select(l => new ReservedItem(l.ProductId, l.WarehouseId, l.Quantity))
                    .ToList();

                return new List<Event> { new StockReservedEvent(request.OrderId, published) };
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

        routeBuilder.MapGet("/health", () => TypedResults.Ok(new { status = "healthy" }));
    }
}
