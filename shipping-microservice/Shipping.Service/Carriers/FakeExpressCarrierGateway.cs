using System.Text.Json;
using Shipping.Service.Models;

namespace Shipping.Service.Carriers;

internal sealed class FakeExpressCarrierGateway : ICarrierGateway
{
    public const string Key = "fake-express";

    private static readonly TimeSpan AcceptedWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InTransitWindow = TimeSpan.FromMinutes(30);

    private readonly FakeCarrierDispatchRegistry _registry;
    private readonly TimeProvider _timeProvider;

    public FakeExpressCarrierGateway()
        : this(new FakeCarrierDispatchRegistry(), TimeProvider.System)
    {
    }

    public FakeExpressCarrierGateway(FakeCarrierDispatchRegistry registry, TimeProvider timeProvider)
    {
        _registry = registry;
        _timeProvider = timeProvider;
    }

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
