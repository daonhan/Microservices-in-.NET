namespace Payment.Service.Features.RefundPayment;

public record PaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    decimal Amount,
    string Currency,
    string Status,
    string? ProviderReference,
    DateTime CreatedAt,
    DateTime UpdatedAt);
