namespace Payment.Service.Infrastructure.Gateways;

/// <summary>
/// Deterministic, in-memory payment provider for non-production environments.
///
/// Outcome is determined by the cents portion of the amount:
/// <list type="bullet">
///   <item><description><c>.99</c> — decline (used by failure-compensation tests)</description></item>
///   <item><description>any other cents value (including <c>.00</c>) — success</description></item>
/// </list>
/// Capture and refund always succeed when given a non-empty reference.
/// </summary>
public class InMemoryPaymentGateway : IPaymentGateway
{
    public Task<PaymentGatewayResult> AuthorizeAsync(decimal amount, string currency, string reference)
    {
        var cents = (int)Math.Round((amount - Math.Truncate(amount)) * 100m);

        if (cents == 99)
        {
            return Task.FromResult(new PaymentGatewayResult(
                Success: false,
                ProviderReference: null,
                FailureReason: "Card declined by issuer"));
        }

        return Task.FromResult(new PaymentGatewayResult(
            Success: true,
            ProviderReference: $"INMEM-{Guid.NewGuid():N}",
            FailureReason: null));
    }

    public Task<PaymentGatewayResult> CaptureAsync(string reference)
    {
        return Task.FromResult(new PaymentGatewayResult(
            Success: true,
            ProviderReference: reference,
            FailureReason: null));
    }

    public Task<PaymentGatewayResult> RefundAsync(string reference, decimal amount)
    {
        return Task.FromResult(new PaymentGatewayResult(
            Success: true,
            ProviderReference: reference,
            FailureReason: null));
    }
}
