using System.Security.Claims;
using System.Text.Json;
using System.Transactions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shipping.Service.ApiModels;
using Shipping.Service.Carriers;
using Shipping.Service.Infrastructure.Data;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;
using Shipping.Service.Observability;

namespace Shipping.Service.Endpoints;

public static class ShippingApiEndpoints
{
    private const string AdminRole = "Administrator";
    private const string CustomerIdClaim = "customerId";
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/by-order/{orderId:guid}", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            ClaimsPrincipal user,
            Guid orderId) =>
        {
            var shipments = await shipmentStore.GetByOrder(orderId);

            if (shipments.Count == 0)
            {
                return TypedResults.NotFound($"No shipments found for order {orderId}");
            }

            if (!IsAuthorizedForShipments(user, shipments))
            {
                return TypedResults.Forbid();
            }

            return TypedResults.Ok(shipments.Select(ToResponse).ToList());
        }).RequireAuthorization();

        routeBuilder.MapGet("/{shipmentId:guid}", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            ClaimsPrincipal user,
            Guid shipmentId) =>
        {
            var shipment = await shipmentStore.GetById(shipmentId);

            if (shipment is null)
            {
                return TypedResults.NotFound($"Shipment {shipmentId} not found");
            }

            if (!IsAuthorizedForShipment(user, shipment))
            {
                return TypedResults.Forbid();
            }

            return TypedResults.Ok(ToResponse(shipment));
        }).RequireAuthorization();

        routeBuilder.MapGet("/", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromQuery] string? status,
            [FromQuery] int? warehouseId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? skip,
            [FromQuery] int? take) =>
        {
            ShipmentStatus? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<ShipmentStatus>(status, ignoreCase: true, out var parsed))
                {
                    return TypedResults.BadRequest($"Unknown status '{status}'");
                }

                parsedStatus = parsed;
            }

            var pageSize = Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);
            var pageSkip = Math.Max(skip ?? 0, 0);

            var filters = new ShipmentListFilters(
                Status: parsedStatus,
                WarehouseId: warehouseId,
                From: from,
                To: to,
                Skip: pageSkip,
                Take: pageSize);

            var shipments = await shipmentStore.ListShipments(filters);
            return TypedResults.Ok(shipments.Select(ToResponse).ToList());
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/{shipmentId:guid}/pick", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] IOutboxStore outboxStore,
            [FromServices] ShippingMetrics metrics,
            Guid shipmentId) =>
        {
            return await ApplyTransitionAsync(
                shipmentStore,
                outboxStore,
                metrics,
                shipmentId,
                (shipment, now) => shipment.TryPick(now, ShipmentStatusSource.Admin));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/{shipmentId:guid}/pack", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] IOutboxStore outboxStore,
            [FromServices] ShippingMetrics metrics,
            Guid shipmentId) =>
        {
            return await ApplyTransitionAsync(
                shipmentStore,
                outboxStore,
                metrics,
                shipmentId,
                (shipment, now) => shipment.TryPack(now, ShipmentStatusSource.Admin));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/{shipmentId:guid}/cancel", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] IOutboxStore outboxStore,
            [FromServices] ShippingMetrics metrics,
            Guid shipmentId,
            [FromBody] CancelShipmentRequest? request) =>
        {
            var reason = request?.Reason;
            return await ApplyTransitionAsync(
                shipmentStore,
                outboxStore,
                metrics,
                shipmentId,
                (shipment, now) => shipment.TryCancel(now, ShipmentStatusSource.Admin, reason),
                (shipment, now) => new ShipmentCancelledEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    now,
                    reason));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/{shipmentId:guid}/deliver", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] IOutboxStore outboxStore,
            [FromServices] ShippingMetrics metrics,
            Guid shipmentId) =>
        {
            return await ApplyTransitionAsync(
                shipmentStore,
                outboxStore,
                metrics,
                shipmentId,
                (shipment, now) => shipment.TryDeliver(now, ShipmentStatusSource.Admin),
                (shipment, now) => new ShipmentDeliveredEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    shipment.CarrierKey,
                    shipment.TrackingNumber,
                    now));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/{shipmentId:guid}/fail", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] IOutboxStore outboxStore,
            [FromServices] ShippingMetrics metrics,
            Guid shipmentId,
            [FromBody] FailShipmentRequest? request) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            {
                return TypedResults.BadRequest("Reason is required.");
            }

            var reason = request.Reason;
            return await ApplyTransitionAsync(
                shipmentStore,
                outboxStore,
                metrics,
                shipmentId,
                (shipment, now) => shipment.TryFail(reason, now, ShipmentStatusSource.Admin),
                (shipment, now) => new ShipmentFailedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    shipment.CarrierKey,
                    shipment.TrackingNumber,
                    reason,
                    now));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/{shipmentId:guid}/return", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] IOutboxStore outboxStore,
            [FromServices] ShippingMetrics metrics,
            Guid shipmentId,
            [FromBody] ReturnShipmentRequest? request) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            {
                return TypedResults.BadRequest("Reason is required.");
            }

            var reason = request.Reason;
            return await ApplyTransitionAsync(
                shipmentStore,
                outboxStore,
                metrics,
                shipmentId,
                (shipment, now) => shipment.TryReturn(reason, now, ShipmentStatusSource.Admin),
                (shipment, now) => new ShipmentReturnedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    shipment.CarrierKey,
                    shipment.TrackingNumber,
                    reason,
                    now));
        }).RequireAuthorization("Administrator");

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

        routeBuilder.MapPost("/{shipmentId:guid}/dispatch", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] IOutboxStore outboxStore,
            [FromServices] RateShoppingService rateShopping,
            [FromServices] ShippingMetrics metrics,
            Guid shipmentId,
            [FromBody] DispatchShipmentRequest request) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.CarrierKey) || request.ShippingAddress is null)
            {
                return TypedResults.BadRequest("CarrierKey and ShippingAddress are required.");
            }

            var shipment = await shipmentStore.GetById(shipmentId);
            if (shipment is null)
            {
                return TypedResults.NotFound($"Shipment {shipmentId} not found");
            }

            var carrier = rateShopping.FindCarrier(request.CarrierKey);
            if (carrier is null)
            {
                return TypedResults.BadRequest($"Unknown carrier '{request.CarrierKey}'.");
            }

            var destination = new ShippingAddress(
                Recipient: request.ShippingAddress.Recipient,
                Line1: request.ShippingAddress.Line1,
                Line2: request.ShippingAddress.Line2,
                City: request.ShippingAddress.City,
                State: request.ShippingAddress.State,
                PostalCode: request.ShippingAddress.PostalCode,
                Country: request.ShippingAddress.Country);

            var totalQuantity = shipment.Lines.Sum(l => l.Quantity);

            Money quotedPrice;
            if (request.OverrideQuote is not null)
            {
                quotedPrice = new Money(request.OverrideQuote.PriceAmount, request.OverrideQuote.PriceCurrency);
            }
            else
            {
                var quote = await carrier.QuoteAsync(new ShipmentQuoteRequest(
                    ShipmentId: shipment.Id,
                    WarehouseId: shipment.WarehouseId,
                    Destination: destination,
                    TotalQuantity: totalQuantity));
                quotedPrice = quote.Price;
            }

            var dispatchResult = await carrier.DispatchAsync(new ShipmentDispatchRequest(
                ShipmentId: shipment.Id,
                WarehouseId: shipment.WarehouseId,
                Destination: destination,
                TotalQuantity: totalQuantity));

            var fromStatus = shipment.Status;
            var now = DateTime.UtcNow;

            if (!shipment.TryDispatch(
                carrierKey: carrier.CarrierKey,
                trackingNumber: dispatchResult.TrackingNumber,
                labelRef: dispatchResult.LabelRef,
                quotedPrice: quotedPrice,
                shippingAddress: destination,
                occurredAt: now,
                source: ShipmentStatusSource.Admin))
            {
                return TypedResults.Conflict(new
                {
                    error = "Illegal state transition",
                    currentStatus = shipment.Status.ToString(),
                });
            }

            await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                await shipmentStore.SaveChangesAsync();

                await outboxStore.AddOutboxEvent(new ShipmentDispatchedEvent(
                    ShipmentId: shipment.Id,
                    OrderId: shipment.OrderId,
                    CustomerId: shipment.CustomerId,
                    CarrierKey: carrier.CarrierKey,
                    TrackingNumber: dispatchResult.TrackingNumber,
                    QuotedPriceAmount: quotedPrice.Amount,
                    QuotedPriceCurrency: quotedPrice.Currency,
                    OccurredAt: now));

                await outboxStore.AddOutboxEvent(new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: fromStatus,
                    ToStatus: shipment.Status,
                    OccurredAt: now));

                scope.Complete();
            });

            metrics.RecordStatusChange(shipment.Status);
            metrics.RecordTimeToDispatch(shipment.CreatedAt, now);

            return TypedResults.Ok(new DispatchShipmentResponse(
                ShipmentId: shipment.Id,
                Status: shipment.Status.ToString(),
                CarrierKey: carrier.CarrierKey,
                TrackingNumber: dispatchResult.TrackingNumber,
                LabelRef: dispatchResult.LabelRef,
                QuotedPriceAmount: quotedPrice.Amount,
                QuotedPriceCurrency: quotedPrice.Currency));
        }).RequireAuthorization("Administrator");

        routeBuilder.MapPost("/webhooks/carrier/{carrierKey}", async Task<IResult> (
            [FromServices] IShipmentStore shipmentStore,
            [FromServices] IOutboxStore outboxStore,
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

            await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                var applied = await CarrierStatusApplier.ApplyAsync(
                    shipment,
                    update.Status,
                    ShipmentStatusSource.CarrierWebhook,
                    now,
                    outboxStore,
                    metrics);

                if (applied)
                {
                    await shipmentStore.SaveChangesAsync();
                }

                scope.Complete();
            });

            return TypedResults.Ok(new
            {
                shipmentId = shipment.Id,
                status = shipment.Status.ToString(),
            });
        }).AllowAnonymous();
    }

    private static async Task<IResult> ApplyTransitionAsync(
        IShipmentStore shipmentStore,
        IOutboxStore outboxStore,
        ShippingMetrics metrics,
        Guid shipmentId,
        Func<Shipment, DateTime, bool> transition,
        Func<Shipment, DateTime, ECommerce.Shared.Infrastructure.EventBus.Event>? milestoneFactory = null)
    {
        var shipment = await shipmentStore.GetById(shipmentId);
        if (shipment is null)
        {
            return TypedResults.NotFound($"Shipment {shipmentId} not found");
        }

        var fromStatus = shipment.Status;
        var createdAt = shipment.CreatedAt;
        var now = DateTime.UtcNow;

        if (!transition(shipment, now))
        {
            return TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = shipment.Status.ToString(),
            });
        }

        await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            await shipmentStore.SaveChangesAsync();

            if (milestoneFactory is not null)
            {
                var milestone = milestoneFactory(shipment, now);
                await AddOutboxEventWithRuntimeType(outboxStore, milestone);
            }

            await outboxStore.AddOutboxEvent(new ShipmentStatusChangedEvent(
                shipment.Id,
                shipment.OrderId,
                FromStatus: fromStatus,
                ToStatus: shipment.Status,
                OccurredAt: now));

            scope.Complete();
        });

        metrics.RecordStatusChange(shipment.Status);
        if (shipment.Status == ShipmentStatus.Delivered)
        {
            metrics.RecordTimeToDelivery(createdAt, now);
        }

        return TypedResults.Ok(ToResponse(shipment));
    }

    private static bool IsAuthorizedForShipments(ClaimsPrincipal user, IEnumerable<Shipment> shipments)
        => shipments.All(s => IsAuthorizedForShipment(user, s));

    private static bool IsAuthorizedForShipment(ClaimsPrincipal user, Shipment shipment)
    {
        if (user.HasClaim("user_role", AdminRole))
        {
            return true;
        }

        var customerId = user.FindFirst(CustomerIdClaim)?.Value;
        return customerId is not null && customerId == shipment.CustomerId;
    }

    private static Task AddOutboxEventWithRuntimeType(
        IOutboxStore outboxStore,
        ECommerce.Shared.Infrastructure.EventBus.Event milestone)
    {
        // IOutboxStore.AddOutboxEvent<T>(T) serializes via the generic type;
        // invoking through reflection with the runtime type preserves
        // derived-type properties (e.g. Reason) in the stored payload.
        var method = typeof(IOutboxStore)
            .GetMethod(nameof(IOutboxStore.AddOutboxEvent))!
            .MakeGenericMethod(milestone.GetType());
        return (Task)method.Invoke(outboxStore, [milestone])!;
    }

    private static ShipmentResponse ToResponse(Shipment s)
        => new(
            s.Id,
            s.OrderId,
            s.CustomerId,
            s.WarehouseId,
            s.Status.ToString(),
            s.CreatedAt,
            s.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList());
}
