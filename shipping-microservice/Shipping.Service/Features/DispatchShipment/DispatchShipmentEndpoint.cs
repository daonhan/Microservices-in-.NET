using Microsoft.AspNetCore.Mvc;

namespace Shipping.Service.Features.DispatchShipment;

internal static class DispatchShipmentEndpoint
{
    public static IEndpointRouteBuilder MapDispatchShipment(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{shipmentId:guid}/dispatch", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        DispatchShipmentHandler handler,
        Guid shipmentId,
        [FromBody] DispatchShipmentRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CarrierKey) || request.ShippingAddress is null)
        {
            return TypedResults.BadRequest("CarrierKey and ShippingAddress are required.");
        }

        var outcome = await handler.HandleAsync(shipmentId, request);

        return outcome.Kind switch
        {
            DispatchShipmentOutcomeKind.NotFound => TypedResults.NotFound($"Shipment {shipmentId} not found"),
            DispatchShipmentOutcomeKind.UnknownCarrier => TypedResults.BadRequest($"Unknown carrier '{outcome.UnknownCarrierKey}'."),
            DispatchShipmentOutcomeKind.IllegalTransition => TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = outcome.CurrentStatus!.Value.ToString(),
            }),
            _ => TypedResults.Ok(outcome.Response),
        };
    }
}
