using System.Security.Claims;

namespace Shipping.Service.Features.GetShipmentsByOrder;

internal static class GetShipmentsByOrderEndpoint
{
    private const string AdminRole = "Administrator";
    private const string CustomerIdClaim = "customerId";

    public static IEndpointRouteBuilder MapGetShipmentsByOrder(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/by-order/{orderId:guid}", HandleAsync).RequireAuthorization();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        GetShipmentsByOrderHandler handler,
        ClaimsPrincipal user,
        Guid orderId)
    {
        var shipments = await handler.HandleAsync(orderId);

        if (shipments.Count == 0)
        {
            return TypedResults.NotFound($"No shipments found for order {orderId}");
        }

        if (!IsAuthorizedForShipments(user, shipments))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(shipments);
    }

    private static bool IsAuthorizedForShipments(ClaimsPrincipal user, IEnumerable<ShipmentResponse> shipments)
        => shipments.All(s => IsAuthorizedForShipment(user, s));

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
