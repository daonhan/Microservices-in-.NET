using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Domain.Abstractions;

namespace Inventory.Service.Features.ReserveByHttp;

internal sealed class ReserveByHttpHandler
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public ReserveByHttpHandler(
        IInventoryStore inventoryStore,
        IOutboxUnitOfWork outboxUnitOfWork)
    {
        _inventoryStore = inventoryStore;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task<ReserveResponse?> HandleAsync(int productId, ReserveRequest request)
    {
        ReserveResult? outcome = null;

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            outcome = await _inventoryStore.Reserve(
                request.OrderId,
                [new ReserveLine(productId, request.Quantity)]);

            if (!outcome.Reserved || outcome.AlreadyProcessed)
            {
                return [];
            }

            var published = outcome.Lines
                .Select(l => new ReservedItem(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            return new List<Event> { new StockReservedEvent(request.OrderId, published) };
        });

        if (outcome is null || !outcome.Reserved)
        {
            return null;
        }

        var lines = outcome.Lines
            .Select(l => new ReservedLineDto(l.ProductId, l.WarehouseId, l.Quantity))
            .ToList();

        return new ReserveResponse(request.OrderId, lines);
    }
}
