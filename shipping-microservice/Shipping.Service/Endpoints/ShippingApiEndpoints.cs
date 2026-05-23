using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shipping.Service.ApiModels;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Infrastructure.Carriers;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Endpoints;

public static class ShippingApiEndpoints
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{shipmentId:guid}/quotes", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] RateShoppingService rateShopping,
            [FromServices] ShippingMetrics metrics,
            Guid shipmentId) =>
        {
            var shipment = await shipmentStore.GetById(shipmentId);
            if (shipment is null)
            {
                return TypedResults.NotFound($"Shipment {shipmentId} not found");
            }

            var placeholderAddress = new ShippingAddress(
                Recipient: shipment.CustomerId,
                Line1: "TBD",
                Line2: null,
                City: "TBD",
                State: null,
                PostalCode: "00000",
                Country: "US");

            var totalQuantity = shipment.Lines.Sum(l => l.Quantity);
            var request = new ShipmentQuoteRequest(
                ShipmentId: shipment.Id,
                WarehouseId: shipment.WarehouseId,
                Destination: placeholderAddress,
                TotalQuantity: totalQuantity);

            var quotes = await rateShopping.GetRankedQuotesAsync(request);
            if (quotes.Count >= 2)
            {
                metrics.RecordRateShoppingSpread(
                    minPrice: quotes.Min(q => q.Price.Amount),
                    maxPrice: quotes.Max(q => q.Price.Amount));
            }

            return TypedResults.Ok(quotes.Select(q => new CarrierQuoteResponse(
                q.CarrierKey,
                q.CarrierName,
                q.Price.Amount,
                q.Price.Currency,
                q.EstimatedDeliveryDays)).ToList());
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/webhooks/carrier/{carrierKey}", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] IOutboxUnitOfWork outboxUnitOfWork,
            [FromServices] IEnumerable<ICarrierGateway> carriers,
            [FromServices] IOptions<CarrierWebhookOptions> options,
            [FromServices] ShippingMetrics metrics,
            HttpRequest httpRequest,
            string carrierKey,
            [FromBody] JsonElement payload) =>
        {
            if (string.IsNullOrWhiteSpace(carrierKey))
            {
                return TypedResults.BadRequest("Carrier key required.");
            }

            if (!options.Value.SharedSecrets.TryGetValue(carrierKey, out var expectedSecret)
                || string.IsNullOrWhiteSpace(expectedSecret))
            {
                return TypedResults.Unauthorized();
            }

            if (!httpRequest.Headers.TryGetValue("X-Carrier-Secret", out var presented)
                || !string.Equals(presented.ToString(), expectedSecret, StringComparison.Ordinal))
            {
                return TypedResults.Unauthorized();
            }

            var carrier = carriers.FirstOrDefault(c =>
                string.Equals(c.CarrierKey, carrierKey, StringComparison.OrdinalIgnoreCase));
            if (carrier is null)
            {
                return TypedResults.NotFound($"Unknown carrier '{carrierKey}'.");
            }

            if (!carrier.TryParseWebhookPayload(payload, out var update) || update is null)
            {
                return TypedResults.BadRequest("Unable to parse carrier webhook payload.");
            }

            var shipment = await shipmentStore.GetByTrackingNumber(update.TrackingNumber);
            if (shipment is null)
            {
                return TypedResults.NotFound($"No shipment found for tracking number '{update.TrackingNumber}'.");
            }

            var now = DateTime.UtcNow;

            await outboxUnitOfWork.ExecuteAsync(async () =>
            {
                var events = await CarrierStatusApplier.ApplyAsync(
                    shipment,
                    update.Status,
                    ShipmentStatusSource.CarrierWebhook,
                    now,
                    metrics);

                if (events.Count > 0)
                {
                    await shipmentStore.SaveChangesAsync();
                }

                return events;
            });

            return TypedResults.Ok(new
            {
                shipmentId = shipment.Id,
                status = shipment.Status.ToString(),
            });
        }).AllowAnonymous();
    }
}
