using ECommerce.Shared.Infrastructure.EventBus;

namespace ECommerce.Shared.IntegrationEvents.Commands;

public record CreateShipmentItem(int ProductId, int WarehouseId, int Quantity);

public sealed record CreateShipmentCommand : Command
{
    public CreateShipmentCommand(
        Guid orderId,
        IReadOnlyList<CreateShipmentItem> items,
        Guid? causationId,
        Guid? sagaId)
        : base(
            causationId ?? throw new ArgumentException("Create shipment command causation id cannot be empty.", nameof(causationId)),
            sagaId ?? throw new ArgumentException("Create shipment command saga id cannot be empty.", nameof(sagaId)))
    {
        OrderId = orderId;
        Items = items;
    }

    public Guid OrderId { get; init; }

    public IReadOnlyList<CreateShipmentItem> Items { get; init; }
}
