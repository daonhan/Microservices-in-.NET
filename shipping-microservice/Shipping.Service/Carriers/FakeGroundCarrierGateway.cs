using Shipping.Service.Models;

namespace Shipping.Service.Carriers;

internal sealed class FakeGroundCarrierGateway : ICarrierGateway
{
    public const string Key = "fake-ground";

    public string CarrierKey => Key;

    public string CarrierName => "Fake Ground";

    public Task<CarrierQuote> QuoteAsync(ShipmentQuoteRequest request, CancellationToken cancellationToken = default)
    {
        // Ground: cheaper base, slower delivery.
        var baseRate = 5.75m + request.TotalQuantity * 0.50m;
        return Task.FromResult(new CarrierQuote(
            CarrierKey: CarrierKey,
            CarrierName: CarrierName,
            Price: Money.Usd(decimal.Round(baseRate, 2)),
            EstimatedDeliveryDays: 4));
    }

    public Task<CarrierDispatchResult> DispatchAsync(ShipmentDispatchRequest request, CancellationToken cancellationToken = default)
    {
        var tracking = $"GND-{request.ShipmentId:N}".ToUpperInvariant();
        var label = $"label://{CarrierKey}/{tracking}";
        return Task.FromResult(new CarrierDispatchResult(tracking, label));
    }

    public Task<CarrierStatus> GetStatusAsync(string trackingNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(new CarrierStatus(CarrierStatusCode.Accepted, Detail: null));
}
