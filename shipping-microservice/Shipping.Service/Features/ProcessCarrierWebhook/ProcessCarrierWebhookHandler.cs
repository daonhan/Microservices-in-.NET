using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.Options;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Infrastructure.Carriers;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Features.ProcessCarrierWebhook;

internal sealed class ProcessCarrierWebhookHandler
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly IEnumerable<ICarrierGateway> _carriers;
    private readonly IOptions<CarrierWebhookOptions> _options;
    private readonly ShippingMetrics _metrics;

    public ProcessCarrierWebhookHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        IEnumerable<ICarrierGateway> carriers,
        IOptions<CarrierWebhookOptions> options,
        ShippingMetrics metrics)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _carriers = carriers;
        _options = options;
        _metrics = metrics;
    }

    public async Task<ProcessCarrierWebhookOutcome> HandleAsync(
        string carrierKey,
        string presentedSecret,
        JsonElement payload)
    {
        if (!_options.Value.SharedSecrets.TryGetValue(carrierKey, out var expectedSecret)
            || string.IsNullOrWhiteSpace(expectedSecret))
        {
            return ProcessCarrierWebhookOutcome.Unauthorized();
        }

        if (!string.Equals(presentedSecret, expectedSecret, StringComparison.Ordinal))
        {
            return ProcessCarrierWebhookOutcome.Unauthorized();
        }

        var carrier = _carriers.FirstOrDefault(c =>
            string.Equals(c.CarrierKey, carrierKey, StringComparison.OrdinalIgnoreCase));
        if (carrier is null)
        {
            return ProcessCarrierWebhookOutcome.UnknownCarrier();
        }

        if (!carrier.TryParseWebhookPayload(payload, out var update) || update is null)
        {
            return ProcessCarrierWebhookOutcome.InvalidPayload();
        }

        var shipment = await _shipmentStore.GetByTrackingNumber(update.TrackingNumber);
        if (shipment is null)
        {
            return ProcessCarrierWebhookOutcome.TrackingNotFound(update.TrackingNumber);
        }

        var now = DateTime.UtcNow;

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var events = await CarrierStatusApplier.ApplyAsync(
                shipment,
                update.Status,
                ShipmentStatusSource.CarrierWebhook,
                now,
                _metrics);

            if (events.Count > 0)
            {
                await _shipmentStore.SaveChangesAsync();
            }

            return events;
        });

        return ProcessCarrierWebhookOutcome.Success(shipment.Id, shipment.Status.ToString());
    }
}

internal sealed record ProcessCarrierWebhookOutcome(
    ProcessCarrierWebhookOutcomeKind Kind,
    Guid? ShipmentId,
    string? Status,
    string? TrackingNumber)
{
    public static ProcessCarrierWebhookOutcome Success(Guid shipmentId, string status)
        => new(ProcessCarrierWebhookOutcomeKind.Success, shipmentId, status, null);

    public static ProcessCarrierWebhookOutcome Unauthorized()
        => new(ProcessCarrierWebhookOutcomeKind.Unauthorized, null, null, null);

    public static ProcessCarrierWebhookOutcome UnknownCarrier()
        => new(ProcessCarrierWebhookOutcomeKind.UnknownCarrier, null, null, null);

    public static ProcessCarrierWebhookOutcome InvalidPayload()
        => new(ProcessCarrierWebhookOutcomeKind.InvalidPayload, null, null, null);

    public static ProcessCarrierWebhookOutcome TrackingNotFound(string trackingNumber)
        => new(ProcessCarrierWebhookOutcomeKind.TrackingNotFound, null, null, trackingNumber);
}

internal enum ProcessCarrierWebhookOutcomeKind
{
    Success,
    Unauthorized,
    UnknownCarrier,
    InvalidPayload,
    TrackingNotFound,
}
