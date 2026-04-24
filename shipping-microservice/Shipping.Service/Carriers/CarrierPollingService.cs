using System.Transactions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shipping.Service.Infrastructure.Data;
using Shipping.Service.Models;

namespace Shipping.Service.Carriers;

/// <summary>
/// Periodically polls carriers for status updates on shipments in the
/// <c>Shipped</c> or <c>InTransit</c> state and advances the aggregate
/// accordingly via <see cref="CarrierStatusApplier"/>. Uses
/// <see cref="TimeProvider"/> so tests can drive the tick cadence
/// deterministically.
/// </summary>
internal sealed partial class CarrierPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CarrierWebhookOptions> _options;
    private readonly ILogger<CarrierPollingService> _logger;

    public CarrierPollingService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<CarrierWebhookOptions> options,
        ILogger<CarrierPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Carrier polling tick failed")]
    partial void LogTickFailed(Exception ex);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to poll carrier {CarrierKey} for shipment {ShipmentId}")]
    partial void LogPollFailed(Exception ex, string carrierKey, Guid shipmentId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollingIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
#pragma warning disable CA1031 // top-level background loop must not crash on handler errors
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogTickFailed(ex);
            }

            try
            {
                await Task.Delay(interval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    public async Task<int> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var shipmentStore = scope.ServiceProvider.GetRequiredService<IShipmentStore>();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var carriers = scope.ServiceProvider.GetServices<ICarrierGateway>()
            .ToDictionary(c => c.CarrierKey, StringComparer.OrdinalIgnoreCase);

        var active = await shipmentStore.ListActiveShipments();
        if (active.Count == 0)
        {
            return 0;
        }

        var updated = 0;

        await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var txScope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var changed = false;

            foreach (var shipment in active)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(shipment.CarrierKey)
                    || string.IsNullOrWhiteSpace(shipment.TrackingNumber))
                {
                    continue;
                }

                if (!carriers.TryGetValue(shipment.CarrierKey, out var carrier))
                {
                    continue;
                }

                CarrierStatus status;
                try
                {
                    status = await carrier.GetStatusAsync(shipment.TrackingNumber, cancellationToken);
                }
#pragma warning disable CA1031 // one bad carrier should not fail the whole batch
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogPollFailed(ex, shipment.CarrierKey, shipment.Id);
                    continue;
                }

                var applied = await CarrierStatusApplier.ApplyAsync(
                    shipment,
                    status,
                    ShipmentStatusSource.CarrierPoll,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    outboxStore);

                if (applied)
                {
                    changed = true;
                    updated++;
                }
            }

            if (changed)
            {
                await shipmentStore.SaveChangesAsync();
            }

            txScope.Complete();
        });

        return updated;
    }
}
