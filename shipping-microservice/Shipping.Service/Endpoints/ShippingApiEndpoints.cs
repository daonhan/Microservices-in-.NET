using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Shipping.Service.ApiModels;
using Shipping.Service.Infrastructure.Data;
using Shipping.Service.Models;

namespace Shipping.Service.Endpoints;

public static class ShippingApiEndpoints
{
    private const string AdminRole = "Administrator";
    private const string CustomerIdClaim = "customerId";

    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/by-order/{orderId:guid}", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            ClaimsPrincipal user,
            Guid orderId) =>
        {
            var shipments = await shipmentStore.GetByOrder(orderId);

            if (shipments.Count == 0)
            {
                return TypedResults.NotFound($"No shipments found for order {orderId}");
            }

            if (!IsAuthorizedForShipments(user, shipments))
            {
                return TypedResults.Forbid();
            }

            return TypedResults.Ok(shipments.Select(ToResponse).ToList());
        }).RequireAuthorization();

        routeBuilder.MapGet("/{shipmentId:guid}", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            ClaimsPrincipal user,
            Guid shipmentId) =>
        {
            var shipment = await shipmentStore.GetById(shipmentId);

            if (shipment is null)
            {
                return TypedResults.NotFound($"Shipment {shipmentId} not found");
            }

            if (!IsAuthorizedForShipment(user, shipment))
            {
                return TypedResults.Forbid();
            }

            return TypedResults.Ok(ToResponse(shipment));
        }).RequireAuthorization();
    }

    private static bool IsAuthorizedForShipments(ClaimsPrincipal user, IEnumerable<Shipment> shipments)
        => shipments.All(s => IsAuthorizedForShipment(user, s));

    private static bool IsAuthorizedForShipment(ClaimsPrincipal user, Shipment shipment)
    {
        if (user.HasClaim("user_role", AdminRole))
        {
            return true;
        }

        var customerId = user.FindFirst(CustomerIdClaim)?.Value;
        return customerId is not null && customerId == shipment.CustomerId;
    }

    private static ShipmentResponse ToResponse(Shipment s)
        => new(
            s.Id,
            s.OrderId,
            s.CustomerId,
            s.WarehouseId,
            s.Status.ToString(),
            s.CreatedAt,
            s.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList());
}
