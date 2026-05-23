using Microsoft.AspNetCore.Mvc;
using Shipping.Service.Domain;

namespace Shipping.Service.Features.ListShipments;

internal static class ListShipmentsEndpoint
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public static IEndpointRouteBuilder MapListShipments(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/", HandleAsync).RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        ListShipmentsHandler handler,
        [FromQuery] string? status,
        [FromQuery] int? warehouseId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? skip,
        [FromQuery] int? take)
    {
        ShipmentStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ShipmentStatus>(status, ignoreCase: true, out var parsed))
            {
                return TypedResults.BadRequest($"Unknown status '{status}'");
            }

            parsedStatus = parsed;
        }

        var pageSize = Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);
        var pageSkip = Math.Max(skip ?? 0, 0);

        var filters = new ListShipmentsFilters(
            Status: parsedStatus,
            WarehouseId: warehouseId,
            From: from,
            To: to,
            Skip: pageSkip,
            Take: pageSize);

        var shipments = await handler.HandleAsync(filters);
        return TypedResults.Ok(shipments);
    }
}
