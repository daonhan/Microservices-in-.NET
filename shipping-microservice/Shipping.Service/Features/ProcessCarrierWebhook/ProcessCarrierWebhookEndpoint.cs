using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Shipping.Service.Features.ProcessCarrierWebhook;

internal static class ProcessCarrierWebhookEndpoint
{
    public static IEndpointRouteBuilder MapProcessCarrierWebhook(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/webhooks/carrier/{carrierKey}", HandleAsync).AllowAnonymous();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        ProcessCarrierWebhookHandler handler,
        HttpRequest httpRequest,
        string carrierKey,
        [FromBody] JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(carrierKey))
        {
            return TypedResults.BadRequest("Carrier key required.");
        }

        httpRequest.Headers.TryGetValue("X-Carrier-Secret", out var presented);

        var outcome = await handler.HandleAsync(carrierKey, presented.ToString(), payload);

        return outcome.Kind switch
        {
            ProcessCarrierWebhookOutcomeKind.Unauthorized => TypedResults.Unauthorized(),
            ProcessCarrierWebhookOutcomeKind.UnknownCarrier => TypedResults.NotFound($"Unknown carrier '{carrierKey}'."),
            ProcessCarrierWebhookOutcomeKind.InvalidPayload => TypedResults.BadRequest("Unable to parse carrier webhook payload."),
            ProcessCarrierWebhookOutcomeKind.TrackingNotFound => TypedResults.NotFound($"No shipment found for tracking number '{outcome.TrackingNumber}'."),
            _ => TypedResults.Ok(new
            {
                shipmentId = outcome.ShipmentId,
                status = outcome.Status,
            }),
        };
    }
}
