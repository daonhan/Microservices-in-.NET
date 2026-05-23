using Microsoft.AspNetCore.Mvc;

namespace Shipping.Service.Features.CancelShipment;

internal static class CancelShipmentEndpoint
{
    public static IEndpointRouteBuilder MapCancelShipment(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{shipmentId:guid}/cancel", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        CancelShipmentHandler handler,
        Guid shipmentId,
        [FromBody] CancelShipmentRequest? request)
    {
        var outcome = await handler.HandleAsync(shipmentId, request?.Reason);

        return outcome.Kind switch
        {
            CancelShipmentOutcomeKind.NotFound => TypedResults.NotFound($"Shipment {shipmentId} not found"),
            CancelShipmentOutcomeKind.IllegalTransition => TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = outcome.CurrentStatus!.Value.ToString(),
            }),
            _ => TypedResults.Ok(outcome.Response),
        };
    }
}
