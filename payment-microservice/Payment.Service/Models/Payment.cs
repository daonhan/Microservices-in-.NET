namespace Payment.Service.Models;

public class Payment : Entity
{
    public Guid PaymentId { get; private set; }
    public Guid OrderId { get; private set; }
    public string CustomerId { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public PaymentStatus Status { get; private set; }
    public string? ProviderReference { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Payment() { }

    public static Payment Create(
        Guid paymentId,
        Guid orderId,
        string customerId,
        decimal amount,
        string currency,
        DateTime createdAt)
    {
        return new Payment
        {
            PaymentId = paymentId,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Currency = currency,
            Status = PaymentStatus.Pending,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    public bool Authorize(string providerReference, DateTime occurredAt)
    {
        if (Status == PaymentStatus.Authorized)
        {
            return false;
        }

        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot authorize payment {PaymentId} in status {Status}.");
        }

        ProviderReference = providerReference;
        Status = PaymentStatus.Authorized;
        UpdatedAt = occurredAt;
        Raise(new PaymentAuthorizedDomainEvent(PaymentId, OrderId, CustomerId, Amount, Currency));
        return true;
    }

    public bool Fail(string reason, DateTime occurredAt)
    {
        if (Status == PaymentStatus.Failed)
        {
            return false;
        }

        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot fail payment {PaymentId} in status {Status}.");
        }

        Status = PaymentStatus.Failed;
        UpdatedAt = occurredAt;
        Raise(new PaymentFailedDomainEvent(PaymentId, OrderId, CustomerId, reason));
        return true;
    }

    public bool Capture(DateTime occurredAt)
    {
        if (Status == PaymentStatus.Captured)
        {
            return false;
        }

        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Cannot capture payment {PaymentId} in status {Status}.");
        }

        Status = PaymentStatus.Captured;
        UpdatedAt = occurredAt;
        Raise(new PaymentCapturedDomainEvent(PaymentId, OrderId, Amount));
        return true;
    }

    public bool Refund(decimal refundAmount, DateTime occurredAt)
    {
        if (Status == PaymentStatus.Refunded)
        {
            return false;
        }

        if (Status != PaymentStatus.Captured)
        {
            throw new InvalidOperationException(
                $"Cannot refund payment {PaymentId} in status {Status}.");
        }

        Status = PaymentStatus.Refunded;
        UpdatedAt = occurredAt;
        Raise(new PaymentRefundedDomainEvent(PaymentId, OrderId, refundAmount));
        return true;
    }

    public bool Void(string reason, DateTime occurredAt)
    {
        if (Status == PaymentStatus.Failed)
        {
            return false;
        }

        if (Status != PaymentStatus.Pending && Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Cannot void payment {PaymentId} in status {Status}.");
        }

        Status = PaymentStatus.Failed;
        UpdatedAt = occurredAt;
        Raise(new PaymentFailedDomainEvent(PaymentId, OrderId, CustomerId, reason));
        return true;
    }
}
