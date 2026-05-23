using System.Diagnostics;
using System.Globalization;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Domain;
using Inventory.Service.Domain.Abstractions;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal sealed class ReserveStockCommandHandler : IEventHandler<ReserveStockCommand>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly MetricFactory _metricFactory;

    public ReserveStockCommandHandler(
        IInventoryStore inventoryStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        MetricFactory metricFactory)
    {
        _inventoryStore = inventoryStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metricFactory = metricFactory;
    }

    public async Task Handle(ReserveStockCommand command)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await HandleCore(command);
        }
        finally
        {
            stopwatch.Stop();
            _metricFactory.Histogram("reservation-latency-ms", "ms")
                .Record((int)stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task HandleCore(ReserveStockCommand command)
    {
        if (command.Items is null || command.Items.Count == 0)
        {
            return;
        }

        var lines = command.Items
            .Select(i => new ReserveLine(int.Parse(i.ProductId, CultureInfo.InvariantCulture), i.Quantity))
            .ToList();

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var result = await _inventoryStore.Reserve(command.OrderId, lines);

            if (result.AlreadyProcessed)
            {
                return result.Reserved
                    ? [CreateStockReservedEvent(command, result.Lines)]
                    : [];
            }

            if (!result.Reserved)
            {
                var failed = result.FailedLines
                    .Select(l => new FailedItem(l.ProductId, l.Requested, l.Available))
                    .ToList();

                _metricFactory.Counter("stock-reservations-failed", "reservations").Add(1);

                return [new StockReservationFailedEvent(command.OrderId, failed)
                {
                    CorrelationId = command.CorrelationId,
                    CausationId = command.Id,
                    SagaId = command.SagaId
                }];
            }

            var published = result.Lines
                .Select(l => new ReservedItem(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            foreach (var _ in result.Lines)
            {
                _metricFactory.Counter("stock-movements", "movements")
                    .Add(1, new KeyValuePair<string, object?>("movement_type", nameof(MovementType.Reserve)));
            }

            return [CreateStockReservedEvent(command, published)];
        });
    }

    private static StockReservedEvent CreateStockReservedEvent(
        ReserveStockCommand command,
        IReadOnlyList<ReservedLine> lines)
    {
        var published = lines
            .Select(l => new ReservedItem(l.ProductId, l.WarehouseId, l.Quantity))
            .ToList();

        return CreateStockReservedEvent(command, published);
    }

    private static StockReservedEvent CreateStockReservedEvent(
        ReserveStockCommand command,
        IReadOnlyList<ReservedItem> published)
    {
        var amount = command.Items.Sum(i => i.UnitPrice * i.Quantity);

        return new StockReservedEvent(command.OrderId, published, amount, command.Currency)
        {
            CorrelationId = command.CorrelationId,
            CausationId = command.Id,
            SagaId = command.SagaId
        };
    }
}
