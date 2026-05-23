using Microsoft.AspNetCore.Mvc;

namespace Shipping.Service.Features.ReturnShipment;

internal static class ReturnShipmentEndpoint
{
    public static IEndpointRouteBuilder MapReturnShipment(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{shipmentId:guid}/return", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        ReturnShipmentHandler handler,
        Guid shipmentId,
        [FromBody] ReturnShipmentRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
        {
            return TypedResults.BadRequest("Reason is required.");
        }

        var outcome = await handler.HandleAsync(shipmentId, request.Reason);

        return outcome.Kind switch
        {
            ReturnShipmentOutcomeKind.NotFound => TypedResults.NotFound($"Shipment {shipmentId} not found"),
            ReturnShipmentOutcomeKind.IllegalTransition => TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = outcome.CurrentStatus!.Value.ToString(),
            }),
            _ => TypedResults.Ok(outcome.Response),
        };
    }
}
