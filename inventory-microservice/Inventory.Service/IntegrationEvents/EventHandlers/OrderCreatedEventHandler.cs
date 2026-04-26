using System.Diagnostics;
using System.Globalization;
using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Infrastructure.Data;
using Inventory.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxStore _outboxStore;
    private readonly MetricFactory _metricFactory;

    public OrderCreatedEventHandler(IInventoryStore inventoryStore, IOutboxStore outboxStore, MetricFactory metricFactory)
    {
        _inventoryStore = inventoryStore;
        _outboxStore = outboxStore;
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

        await _outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var result = await _inventoryStore.Reserve(@event.OrderId, lines);

            if (result.AlreadyProcessed)
            {
                scope.Complete();
                return;
            }

            if (!result.Reserved)
            {
                var failed = result.FailedLines
                    .Select(l => new FailedItem(l.ProductId, l.Requested, l.Available))
                    .ToList();

                await _outboxStore.AddOutboxEvent(new StockReservationFailedEvent(@event.OrderId, failed));

                _metricFactory.Counter("stock-reservations-failed", "reservations").Add(1);

                scope.Complete();
                return;
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

            await _outboxStore.AddOutboxEvent(
                new StockReservedEvent(@event.OrderId, published, amount, @event.Currency));

            scope.Complete();
        });
    }
}
