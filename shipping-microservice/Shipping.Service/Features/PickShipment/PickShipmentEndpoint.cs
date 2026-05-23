namespace Shipping.Service.Features.PickShipment;

internal static class PickShipmentEndpoint
{
    public static IEndpointRouteBuilder MapPickShipment(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{shipmentId:guid}/pick", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        PickShipmentHandler handler,
        Guid shipmentId)
    {
        var outcome = await handler.HandleAsync(shipmentId);

        return outcome.Kind switch
        {
            PickShipmentOutcomeKind.NotFound => TypedResults.NotFound($"Shipment {shipmentId} not found"),
            PickShipmentOutcomeKind.IllegalTransition => TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = outcome.CurrentStatus!.Value.ToString(),
            }),
            _ => TypedResults.Ok(outcome.Response),
        };
    }
}
