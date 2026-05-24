using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Infrastructure.Observability;

namespace Payment.Service.Features.CapturePayment;

internal sealed class CapturePaymentHandler
{
    private readonly IPaymentStore _paymentStore;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentMetrics _metrics;

    public CapturePaymentHandler(
        IPaymentStore paymentStore,
        IPaymentGateway gateway,
        PaymentMetrics metrics)
    {
        _paymentStore = paymentStore;
        _gateway = gateway;
        _metrics = metrics;
    }

    public async Task<CaptureResult> HandleAsync(Guid paymentId)
    {
        var payment = await _paymentStore.GetById(paymentId);
        if (payment is null)
        {
            return CaptureResult.NotFound();
        }

        if (payment.Status == PaymentStatus.Captured)
        {
            return CaptureResult.Ok(ToResponse(payment));
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            return CaptureResult.Conflict(payment.Status);
        }

        await _gateway.CaptureAsync(payment.ProviderReference!);

        await _paymentStore.ExecuteAsync(() =>
        {
            payment.Capture(DateTime.UtcNow);
            return Task.CompletedTask;
        });

        _metrics.RecordStatusChange(PaymentStatus.Captured);

        return CaptureResult.Ok(ToResponse(payment));
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

internal sealed record CaptureResult(
    CaptureOutcome Outcome,
    PaymentResponse? Response,
    PaymentStatus? CurrentStatus)
{
    public static CaptureResult NotFound() => new(CaptureOutcome.NotFound, null, null);
    public static CaptureResult Conflict(PaymentStatus currentStatus) => new(CaptureOutcome.Conflict, null, currentStatus);
    public static CaptureResult Ok(PaymentResponse response) => new(CaptureOutcome.Ok, response, null);
}

internal enum CaptureOutcome
{
    Ok,
    NotFound,
    Conflict,
}
