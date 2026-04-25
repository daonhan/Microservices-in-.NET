namespace Payment.Service.Models;

public class Payment
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
}
