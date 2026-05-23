namespace Shipping.Service.Features.PackShipment;

internal static class PackShipmentEndpoint
{
    public static IEndpointRouteBuilder MapPackShipment(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{shipmentId:guid}/pack", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        PackShipmentHandler handler,
        Guid shipmentId)
    {
        var outcome = await handler.HandleAsync(shipmentId);

        return outcome.Kind switch
        {
            PackShipmentOutcomeKind.NotFound => TypedResults.NotFound($"Shipment {shipmentId} not found"),
            PackShipmentOutcomeKind.IllegalTransition => TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = outcome.CurrentStatus!.Value.ToString(),
            }),
            _ => TypedResults.Ok(outcome.Response),
        };
    }
}
