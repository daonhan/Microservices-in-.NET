using ECommerce.Shared.Infrastructure.EventBus;

namespace ECommerce.Shared.IntegrationEvents.Commands;

public sealed record ConfirmOrderCommand : Command
{
    public ConfirmOrderCommand(
        Guid orderId,
        Guid? causationId,
        Guid? sagaId)
        : base(
            causationId ?? throw new ArgumentException("Confirm order command causation id cannot be empty.", nameof(causationId)),
            sagaId ?? throw new ArgumentException("Confirm order command saga id cannot be empty.", nameof(sagaId)))
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; init; }
}
