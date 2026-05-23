using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Infrastructure.Carriers;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Features.DispatchShipment;

internal sealed class DispatchShipmentHandler
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly RateShoppingService _rateShopping;
    private readonly ShippingMetrics _metrics;

    public DispatchShipmentHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        RateShoppingService rateShopping,
        ShippingMetrics metrics)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _rateShopping = rateShopping;
        _metrics = metrics;
    }

    public async Task<DispatchShipmentOutcome> HandleAsync(Guid shipmentId, DispatchShipmentRequest request)
    {
        var shipment = await _shipmentStore.GetById(shipmentId);
        if (shipment is null)
        {
            return DispatchShipmentOutcome.NotFound();
        }

        var carrier = _rateShopping.FindCarrier(request.CarrierKey);
        if (carrier is null)
        {
            return DispatchShipmentOutcome.UnknownCarrier(request.CarrierKey);
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
            return DispatchShipmentOutcome.IllegalTransition(shipment.Status);
        }

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            await _shipmentStore.SaveChangesAsync();

            return new Event[]
            {
                new ShipmentDispatchedEvent(
                    ShipmentId: shipment.Id,
                    OrderId: shipment.OrderId,
                    CustomerId: shipment.CustomerId,
                    CarrierKey: carrier.CarrierKey,
                    TrackingNumber: dispatchResult.TrackingNumber,
                    QuotedPriceAmount: quotedPrice.Amount,
                    QuotedPriceCurrency: quotedPrice.Currency,
                    OccurredAt: now),
                new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: (int?)fromStatus,
                    ToStatus: (int)shipment.Status,
                    OccurredAt: now),
            };
        });

        _metrics.RecordStatusChange(shipment.Status);
        _metrics.RecordTimeToDispatch(shipment.CreatedAt, now);

        return DispatchShipmentOutcome.Success(new DispatchShipmentResponse(
            ShipmentId: shipment.Id,
            Status: shipment.Status.ToString(),
            CarrierKey: carrier.CarrierKey,
            TrackingNumber: dispatchResult.TrackingNumber,
            LabelRef: dispatchResult.LabelRef,
            QuotedPriceAmount: quotedPrice.Amount,
            QuotedPriceCurrency: quotedPrice.Currency));
    }
}

internal sealed record DispatchShipmentOutcome(
    DispatchShipmentOutcomeKind Kind,
    DispatchShipmentResponse? Response,
    ShipmentStatus? CurrentStatus,
    string? UnknownCarrierKey)
{
    public static DispatchShipmentOutcome Success(DispatchShipmentResponse response)
        => new(DispatchShipmentOutcomeKind.Success, response, null, null);

    public static DispatchShipmentOutcome NotFound()
        => new(DispatchShipmentOutcomeKind.NotFound, null, null, null);

    public static DispatchShipmentOutcome UnknownCarrier(string carrierKey)
        => new(DispatchShipmentOutcomeKind.UnknownCarrier, null, null, carrierKey);

    public static DispatchShipmentOutcome IllegalTransition(ShipmentStatus currentStatus)
        => new(DispatchShipmentOutcomeKind.IllegalTransition, null, currentStatus, null);
}

internal enum DispatchShipmentOutcomeKind
{
    Success,
    NotFound,
    UnknownCarrier,
    IllegalTransition,
}
