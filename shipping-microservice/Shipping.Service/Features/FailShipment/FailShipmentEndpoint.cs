using Microsoft.AspNetCore.Mvc;

namespace Shipping.Service.Features.FailShipment;

internal static class FailShipmentEndpoint
{
    public static IEndpointRouteBuilder MapFailShipment(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{shipmentId:guid}/fail", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        FailShipmentHandler handler,
        Guid shipmentId,
        [FromBody] FailShipmentRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
        {
            return TypedResults.BadRequest("Reason is required.");
        }

        var outcome = await handler.HandleAsync(shipmentId, request.Reason);

        return outcome.Kind switch
        {
            FailShipmentOutcomeKind.NotFound => TypedResults.NotFound($"Shipment {shipmentId} not found"),
            FailShipmentOutcomeKind.IllegalTransition => TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = outcome.CurrentStatus!.Value.ToString(),
            }),
            _ => TypedResults.Ok(outcome.Response),
        };
    }
}
