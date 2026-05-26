using ECommerce.Shared.Infrastructure.EventBus;

namespace ECommerce.Shared.IntegrationEvents.Commands;

public sealed record CommitStockCommand : Command
{
    public CommitStockCommand(
        Guid orderId,
        Guid? causationId,
        Guid? sagaId)
        : base(
            causationId ?? throw new ArgumentException("Commit stock command causation id cannot be empty.", nameof(causationId)),
            sagaId ?? throw new ArgumentException("Commit stock command saga id cannot be empty.", nameof(sagaId)))
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; init; }
}
