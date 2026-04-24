using System.Text.Json;
using Shipping.Service.Models;

namespace Shipping.Service.Carriers;

internal sealed class FakeGroundCarrierGateway : ICarrierGateway
{
    public const string Key = "fake-ground";

    private static readonly TimeSpan AcceptedWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan InTransitWindow = TimeSpan.FromMinutes(60);

    private readonly FakeCarrierDispatchRegistry _registry;
    private readonly TimeProvider _timeProvider;

    public FakeGroundCarrierGateway()
        : this(new FakeCarrierDispatchRegistry(), TimeProvider.System)
    {
    }

    public FakeGroundCarrierGateway(FakeCarrierDispatchRegistry registry, TimeProvider timeProvider)
    {
        _registry = registry;
        _timeProvider = timeProvider;
    }

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
        _registry.Record(tracking, _timeProvider.GetUtcNow());
        return Task.FromResult(new CarrierDispatchResult(tracking, label));
    }

    public Task<CarrierStatus> GetStatusAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGet(trackingNumber, out var dispatchedAt))
        {
            return Task.FromResult(new CarrierStatus(CarrierStatusCode.Unknown, Detail: null));
        }

        var elapsed = _timeProvider.GetUtcNow() - dispatchedAt;
        var code = elapsed switch
        {
            _ when elapsed < AcceptedWindow => CarrierStatusCode.Accepted,
            _ when elapsed < InTransitWindow => CarrierStatusCode.InTransit,
            _ => CarrierStatusCode.Delivered,
        };
        return Task.FromResult(new CarrierStatus(code, Detail: null));
    }

    public bool TryParseWebhookPayload(JsonElement payload, out CarrierWebhookUpdate? update)
        => FakeCarrierWebhookParser.TryParse(payload, out update);
}
