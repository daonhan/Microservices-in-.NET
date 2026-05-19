using ECommerce.Shared.Infrastructure.EventBus;

namespace ECommerce.Shared.IntegrationEvents.Commands;

public sealed record RefundPaymentCommand : Command
{
    public RefundPaymentCommand(
        Guid orderId,
        decimal amount,
        Guid? causationId,
        Guid? sagaId)
        : base(
            causationId ?? throw new ArgumentException("Refund payment command causation id cannot be empty.", nameof(causationId)),
            sagaId ?? throw new ArgumentException("Refund payment command saga id cannot be empty.", nameof(sagaId)))
    {
        OrderId = orderId;
        Amount = amount;
    }

    public Guid OrderId { get; init; }

    public decimal Amount { get; init; }
}
