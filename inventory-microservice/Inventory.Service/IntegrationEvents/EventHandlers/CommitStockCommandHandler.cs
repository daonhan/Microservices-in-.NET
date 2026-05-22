using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Infrastructure.Data;
using Inventory.Service.Models;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal sealed class CommitStockCommandHandler : IEventHandler<CommitStockCommand>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly MetricFactory _metricFactory;

    public CommitStockCommandHandler(
        IInventoryStore inventoryStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        MetricFactory metricFactory)
    {
        _inventoryStore = inventoryStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metricFactory = metricFactory;
    }

    public async Task Handle(CommitStockCommand command)
    {
        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var result = await _inventoryStore.CommitReservations(command.OrderId);

            if (!result.Committed)
            {
                return [];
            }

            if (!result.AlreadyProcessed)
            {
                foreach (var _ in result.Lines)
                {
                    _metricFactory.Counter("stock-movements", "movements")
                        .Add(1, new KeyValuePair<string, object?>("movement_type", nameof(MovementType.Commit)));
                }
            }

            var published = result.Lines
                .Select(l => new CommittedItem(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            return [new StockCommittedEvent(command.OrderId, published)
            {
                CorrelationId = command.CorrelationId,
                CausationId = command.Id,
                SagaId = command.SagaId,
            }];
        });
    }
}
