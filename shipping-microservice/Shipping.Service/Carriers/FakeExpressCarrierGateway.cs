using Shipping.Service.Models;

namespace Shipping.Service.Carriers;

internal sealed class FakeExpressCarrierGateway : ICarrierGateway
{
    public const string Key = "fake-express";

    public string CarrierKey => Key;

    public string CarrierName => "Fake Express";

    public Task<CarrierQuote> QuoteAsync(ShipmentQuoteRequest request, CancellationToken cancellationToken = default)
    {
        // Express: premium price, fast delivery.
        var baseRate = 12.50m + request.TotalQuantity * 1.25m;
        return Task.FromResult(new CarrierQuote(
            CarrierKey: CarrierKey,
            CarrierName: CarrierName,
            Price: Money.Usd(decimal.Round(baseRate, 2)),
            EstimatedDeliveryDays: 1));
    }

    public Task<CarrierDispatchResult> DispatchAsync(ShipmentDispatchRequest request, CancellationToken cancellationToken = default)
    {
        var tracking = $"EXP-{request.ShipmentId:N}".ToUpperInvariant();
        var label = $"label://{CarrierKey}/{tracking}";
        return Task.FromResult(new CarrierDispatchResult(tracking, label));
    }

    public Task<CarrierStatus> GetStatusAsync(string trackingNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(new CarrierStatus(CarrierStatusCode.Accepted, Detail: null));
}
