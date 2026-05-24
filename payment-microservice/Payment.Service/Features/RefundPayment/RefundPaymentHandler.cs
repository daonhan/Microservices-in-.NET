using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Infrastructure.Observability;

namespace Payment.Service.Features.RefundPayment;

internal sealed class RefundPaymentHandler
{
    private readonly IPaymentStore _paymentStore;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentMetrics _metrics;

    public RefundPaymentHandler(
        IPaymentStore paymentStore,
        IPaymentGateway gateway,
        PaymentMetrics metrics)
    {
        _paymentStore = paymentStore;
        _gateway = gateway;
        _metrics = metrics;
    }

    public async Task<RefundResult> HandleAsync(Guid paymentId, RefundPaymentRequest? request)
    {
        var payment = await _paymentStore.GetById(paymentId);
        if (payment is null)
        {
            return RefundResult.NotFound();
        }

        if (payment.Status != PaymentStatus.Captured)
        {
            return RefundResult.Conflict(payment.Status);
        }

        var refundAmount = request?.Amount ?? payment.Amount;

        await _gateway.RefundAsync(payment.ProviderReference!, refundAmount);

        await _paymentStore.ExecuteAsync(() =>
        {
            payment.Refund(refundAmount, DateTime.UtcNow);
            return Task.CompletedTask;
        });

        _metrics.RecordStatusChange(PaymentStatus.Refunded);

        return RefundResult.Ok(ToResponse(payment));
    }

    private static PaymentResponse ToResponse(Domain.Payment payment)
        => new(
            payment.PaymentId,
            payment.OrderId,
            payment.CustomerId,
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.ProviderReference,
            payment.CreatedAt,
            payment.UpdatedAt);
}

internal sealed record RefundResult(
    RefundOutcome Outcome,
    PaymentResponse? Response,
    PaymentStatus? CurrentStatus)
{
    public static RefundResult NotFound() => new(RefundOutcome.NotFound, null, null);
    public static RefundResult Conflict(PaymentStatus currentStatus) => new(RefundOutcome.Conflict, null, currentStatus);
    public static RefundResult Ok(PaymentResponse response) => new(RefundOutcome.Ok, response, null);
}

internal enum RefundOutcome
{
    Ok,
    NotFound,
    Conflict,
}
