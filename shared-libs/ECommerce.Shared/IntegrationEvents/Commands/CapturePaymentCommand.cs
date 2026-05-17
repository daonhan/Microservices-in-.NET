using ECommerce.Shared.Infrastructure.EventBus;

namespace ECommerce.Shared.IntegrationEvents.Commands;

public sealed record CapturePaymentCommand : Command
{
    public CapturePaymentCommand(
        Guid orderId,
        Guid paymentId,
        decimal amount,
        Guid? causationId,
        Guid? sagaId)
        : base(
            causationId ?? throw new ArgumentException("Capture payment command causation id cannot be empty.", nameof(causationId)),
            sagaId ?? throw new ArgumentException("Capture payment command saga id cannot be empty.", nameof(sagaId)))
    {
        OrderId = orderId;
        PaymentId = paymentId;
        Amount = amount;
    }

    public Guid OrderId { get; init; }

    public Guid PaymentId { get; init; }

    public decimal Amount { get; init; }
}
