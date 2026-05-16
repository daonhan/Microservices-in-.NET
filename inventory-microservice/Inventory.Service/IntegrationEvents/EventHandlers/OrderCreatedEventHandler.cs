using System.Diagnostics;
using System.Globalization;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Infrastructure.Data;
using Inventory.Service.Models;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly MetricFactory _metricFactory;

    public OrderCreatedEventHandler(
        IInventoryStore inventoryStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        MetricFactory metricFactory)
    {
        _inventoryStore = inventoryStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metricFactory = metricFactory;
    }

    public async Task Handle(OrderCreatedEvent @event)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await HandleCore(@event);
        }
        finally
        {
            stopwatch.Stop();
            _metricFactory.Histogram("reservation-latency-ms", "ms")
                .Record((int)stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task HandleCore(OrderCreatedEvent @event)
    {
        if (@event.Items is null || @event.Items.Count == 0)
        {
            return;
        }

        var lines = @event.Items
            .Select(i => new ReserveLine(int.Parse(i.ProductId, CultureInfo.InvariantCulture), i.Quantity))
            .ToList();

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var result = await _inventoryStore.Reserve(@event.OrderId, lines);

            if (result.AlreadyProcessed)
            {
                return [];
            }

            if (!result.Reserved)
            {
                var failed = result.FailedLines
                    .Select(l => new FailedItem(l.ProductId, l.Requested, l.Available))
                    .ToList();

                _metricFactory.Counter("stock-reservations-failed", "reservations").Add(1);

                return [new StockReservationFailedEvent(@event.OrderId, failed)];
            }

            var published = result.Lines
                .Select(l => new ReservedItem(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            foreach (var _ in result.Lines)
            {
                _metricFactory.Counter("stock-movements", "movements")
                    .Add(1, new KeyValuePair<string, object?>("movement_type", nameof(MovementType.Reserve)));
            }

            var amount = @event.Items.Sum(i => i.UnitPrice * i.Quantity);

            return [new StockReservedEvent(@event.OrderId, published, amount, @event.Currency)];
        });
    }
}
