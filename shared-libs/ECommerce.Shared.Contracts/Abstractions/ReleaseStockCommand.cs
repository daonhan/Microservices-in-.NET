using ECommerce.Shared.Infrastructure.EventBus;

namespace ECommerce.Shared.IntegrationEvents.Commands;

public sealed record ReleaseStockCommand : Command
{
    public ReleaseStockCommand(
        Guid orderId,
        Guid? causationId,
        Guid? sagaId)
        : base(
            causationId ?? throw new ArgumentException("Release stock command causation id cannot be empty.", nameof(causationId)),
            sagaId ?? throw new ArgumentException("Release stock command saga id cannot be empty.", nameof(sagaId)))
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; init; }
}
