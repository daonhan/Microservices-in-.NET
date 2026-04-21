using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.ApiModels;
using Inventory.Service.IntegrationEvents;
using Inventory.Service.IntegrationEvents.EventHandlers;
using Inventory.Service.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Api;

public class ObservabilityTests : IntegrationTestBase
{
    public ObservabilityTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Restock_IncrementsStockMovementsCounter_TaggedByMovementType()
    {
        // Arrange
        const int productId = 9001;
        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 0,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 0,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        var observed = new List<(int value, string? movementType)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Inventory" && instrument.Name == "stock-movements")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, _) =>
        {
            string? movementType = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "movement_type")
                {
                    movementType = tag.Value as string;
                }
            }
            observed.Add((measurement, movementType));
        });
        listener.Start();

        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync($"/{productId}/restock", new RestockRequest(3));

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Contains(observed, o => o.value == 1 && o.movementType == nameof(MovementType.Restock));
    }

    [Fact]
    public async Task OrderCreatedEventHandler_RecordsReservationLatencyHistogramSample()
    {
        // Arrange
        var observed = new List<int>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Inventory" && instrument.Name == "reservation-latency-ms")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((_, measurement, _, _) => observed.Add(measurement));
        listener.Start();

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<OrderCreatedEventHandler>(scope.ServiceProvider);

        // Act — empty items exits early but still runs through the try/finally
        await handler.Handle(new OrderCreatedEvent(Guid.NewGuid(), "customer-1", []));

        // Assert
        Assert.NotEmpty(observed);
    }

    [Fact]
    public async Task PrometheusScrape_ExposesCustomInventoryMetrics()
    {
        // Arrange — emit at least one measurement for each metric so they appear on scrape
        var metricFactory = Factory.Services.GetRequiredService<MetricFactory>();
        metricFactory.Counter("stock-movements", "movements")
            .Add(1, new KeyValuePair<string, object?>("movement_type", nameof(MovementType.Restock)));
        metricFactory.Histogram("reservation-latency-ms", "ms").Record(1);
        metricFactory.Counter("stock-depleted", "events").Add(1);
        metricFactory.Counter("stock-reservations-failed", "reservations").Add(1);

        // Act
        var response = await HttpClient.GetAsync("/metrics");

        // Assert
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("stock_movements", body);
        Assert.Contains("movement_type=\"Restock\"", body);
        Assert.Contains("reservation_latency_ms", body);
        Assert.Contains("stock_depleted", body);
        Assert.Contains("stock_reservations_failed", body);
    }
}
