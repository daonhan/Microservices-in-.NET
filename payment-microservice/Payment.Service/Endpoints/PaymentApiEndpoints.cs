namespace Payment.Service.Endpoints;

public static class PaymentApiEndpoints
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
    }

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

    public record RefundPaymentRequest(decimal? Amount);
}
