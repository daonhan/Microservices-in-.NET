using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Infrastructure.Carriers;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Features.GetCarrierQuotes;

internal sealed class GetCarrierQuotesHandler
{
    private readonly IShipmentStore _shipmentStore;
    private readonly RateShoppingService _rateShopping;
    private readonly ShippingMetrics _metrics;

    public GetCarrierQuotesHandler(
        IShipmentStore shipmentStore,
        RateShoppingService rateShopping,
        ShippingMetrics metrics)
    {
        _shipmentStore = shipmentStore;
        _rateShopping = rateShopping;
        _metrics = metrics;
    }

    public async Task<GetCarrierQuotesOutcome> HandleAsync(Guid shipmentId)
    {
        var shipment = await _shipmentStore.GetById(shipmentId);
        if (shipment is null)
        {
            return GetCarrierQuotesOutcome.NotFound();
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

        var quotes = await _rateShopping.GetRankedQuotesAsync(request);
        if (quotes.Count >= 2)
        {
            _metrics.RecordRateShoppingSpread(
                minPrice: quotes.Min(q => q.Price.Amount),
                maxPrice: quotes.Max(q => q.Price.Amount));
        }

        var response = quotes.Select(q => new CarrierQuoteResponse(
            q.CarrierKey,
            q.CarrierName,
            q.Price.Amount,
            q.Price.Currency,
            q.EstimatedDeliveryDays)).ToList();

        return GetCarrierQuotesOutcome.Success(response);
    }
}

internal sealed record GetCarrierQuotesOutcome(
    GetCarrierQuotesOutcomeKind Kind,
    IReadOnlyList<CarrierQuoteResponse>? Quotes)
{
    public static GetCarrierQuotesOutcome Success(IReadOnlyList<CarrierQuoteResponse> quotes)
        => new(GetCarrierQuotesOutcomeKind.Success, quotes);

    public static GetCarrierQuotesOutcome NotFound()
        => new(GetCarrierQuotesOutcomeKind.NotFound, null);
}

internal enum GetCarrierQuotesOutcomeKind
{
    Success,
    NotFound,
}
