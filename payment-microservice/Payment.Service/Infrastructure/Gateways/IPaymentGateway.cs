namespace Payment.Service.Infrastructure.Gateways;

public interface IPaymentGateway
{
    Task<PaymentGatewayResult> AuthorizeAsync(decimal amount, string currency, string reference);
    Task<PaymentGatewayResult> CaptureAsync(string reference);
    Task<PaymentGatewayResult> RefundAsync(string reference, decimal amount);
}

public record PaymentGatewayResult(bool Success, string? ProviderReference, string? FailureReason);
