namespace Shipping.Service.Features.GetCarrierQuotes;

internal static class GetCarrierQuotesEndpoint
{
    public static IEndpointRouteBuilder MapGetCarrierQuotes(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{shipmentId:guid}/quotes", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        GetCarrierQuotesHandler handler,
        Guid shipmentId)
    {
        var outcome = await handler.HandleAsync(shipmentId);

        return outcome.Kind switch
        {
            GetCarrierQuotesOutcomeKind.NotFound => TypedResults.NotFound($"Shipment {shipmentId} not found"),
            _ => TypedResults.Ok(outcome.Quotes),
        };
    }
}
