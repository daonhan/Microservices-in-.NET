namespace Shipping.Service.Features.DeliverShipment;

internal static class DeliverShipmentEndpoint
{
    public static IEndpointRouteBuilder MapDeliverShipment(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{shipmentId:guid}/deliver", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        DeliverShipmentHandler handler,
        Guid shipmentId)
    {
        var outcome = await handler.HandleAsync(shipmentId);

        return outcome.Kind switch
        {
            DeliverShipmentOutcomeKind.NotFound => TypedResults.NotFound($"Shipment {shipmentId} not found"),
            DeliverShipmentOutcomeKind.IllegalTransition => TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = outcome.CurrentStatus!.Value.ToString(),
            }),
            _ => TypedResults.Ok(outcome.Response),
        };
    }
}
