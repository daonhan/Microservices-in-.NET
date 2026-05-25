using ECommerce.Shared.Infrastructure.EventBus;

namespace ECommerce.Shared.IntegrationEvents.Commands;

public sealed record VoidPaymentCommand : Command
{
    public VoidPaymentCommand(
        Guid orderId,
        string reason,
        Guid? causationId,
        Guid? sagaId)
        : base(
            causationId ?? throw new ArgumentException("Void payment command causation id cannot be empty.", nameof(causationId)),
            sagaId ?? throw new ArgumentException("Void payment command saga id cannot be empty.", nameof(sagaId)))
    {
        OrderId = orderId;
        Reason = reason;
    }

    public Guid OrderId { get; init; }

    public string Reason { get; init; }
}
