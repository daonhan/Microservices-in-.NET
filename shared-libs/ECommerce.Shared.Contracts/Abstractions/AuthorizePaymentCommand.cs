using ECommerce.Shared.Infrastructure.EventBus;

namespace ECommerce.Shared.IntegrationEvents.Commands;

public sealed record AuthorizePaymentCommand : Command
{
    public AuthorizePaymentCommand(
        Guid orderId,
        decimal amount,
        string currency,
        Guid? causationId,
        Guid? sagaId)
        : base(
            causationId ?? throw new ArgumentException("Authorize payment command causation id cannot be empty.", nameof(causationId)),
            sagaId ?? throw new ArgumentException("Authorize payment command saga id cannot be empty.", nameof(sagaId)))
    {
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
    }

    public Guid OrderId { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; }
}
