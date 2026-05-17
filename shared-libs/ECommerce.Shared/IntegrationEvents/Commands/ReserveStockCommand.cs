using ECommerce.Shared.Infrastructure.EventBus;

namespace ECommerce.Shared.IntegrationEvents.Commands;

public record ReserveStockItem(string ProductId, int Quantity, decimal UnitPrice = 0m);

public sealed record ReserveStockCommand : Command
{
    public ReserveStockCommand(
        Guid orderId,
        string customerId,
        IReadOnlyList<ReserveStockItem> items,
        string currency,
        Guid? causationId,
        Guid? sagaId)
        : base(
            causationId ?? throw new ArgumentException("Reserve stock command causation id cannot be empty.", nameof(causationId)),
            sagaId ?? throw new ArgumentException("Reserve stock command saga id cannot be empty.", nameof(sagaId)))
    {
        OrderId = orderId;
        CustomerId = customerId;
        Items = items;
        Currency = currency;
    }

    public Guid OrderId { get; init; }

    public string CustomerId { get; init; }

    public IReadOnlyList<ReserveStockItem> Items { get; init; }

    public string Currency { get; init; }
}
