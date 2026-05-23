using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Domain;
using Inventory.Service.Domain.Abstractions;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal sealed class ReleaseStockCommandHandler : IEventHandler<ReleaseStockCommand>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly MetricFactory _metricFactory;

    public ReleaseStockCommandHandler(
        IInventoryStore inventoryStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        MetricFactory metricFactory)
    {
        _inventoryStore = inventoryStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metricFactory = metricFactory;
    }

    public async Task Handle(ReleaseStockCommand command)
    {
        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var result = await _inventoryStore.ReleaseReservations(command.OrderId);

            if (result.Released && !result.AlreadyProcessed)
            {
                foreach (var _ in result.Lines)
                {
                    _metricFactory.Counter("stock-movements", "movements")
                        .Add(1, new KeyValuePair<string, object?>("movement_type", nameof(MovementType.Release)));
                }
            }

            var published = result.Lines
                .Select(l => new ReleasedItem(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            return [new StockReleasedEvent(command.OrderId, published)
            {
                CorrelationId = command.CorrelationId,
                CausationId = command.Id,
                SagaId = command.SagaId,
            }];
        });
    }
}
