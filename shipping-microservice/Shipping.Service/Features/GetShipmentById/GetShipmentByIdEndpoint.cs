using System.Security.Claims;
using Shipping.Service.Features.GetShipmentsByOrder;

namespace Shipping.Service.Features.GetShipmentById;

internal static class GetShipmentByIdEndpoint
{
    private const string AdminRole = "Administrator";
    private const string CustomerIdClaim = "customerId";

    public static IEndpointRouteBuilder MapGetShipmentById(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{shipmentId:guid}", HandleAsync).RequireAuthorization();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        GetShipmentByIdHandler handler,
        ClaimsPrincipal user,
        Guid shipmentId)
    {
        var shipment = await handler.HandleAsync(shipmentId);

        if (shipment is null)
        {
            return TypedResults.NotFound($"Shipment {shipmentId} not found");
        }

        if (!IsAuthorizedForShipment(user, shipment))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(shipment);
    }

    private static bool IsAuthorizedForShipment(ClaimsPrincipal user, ShipmentResponse shipment)
    {
        if (user.HasClaim("user_role", AdminRole))
        {
            return true;
        }

        var customerId = user.FindFirst(CustomerIdClaim)?.Value;
        return customerId is not null && customerId == shipment.CustomerId;
    }
}
