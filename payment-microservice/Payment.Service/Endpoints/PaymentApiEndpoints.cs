using Microsoft.AspNetCore.Mvc;
using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Infrastructure.Observability;

namespace Payment.Service.Endpoints;

public static class PaymentApiEndpoints
{
    private const string AdminPolicy = "Administrator";

    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{paymentId:guid}/capture", async Task<IResult> (
            [FromServices] IPaymentStore paymentStore,
            [FromServices] IPaymentGateway gateway,
            [FromServices] PaymentMetrics metrics,
            Guid paymentId) =>
        {
            var payment = await paymentStore.GetById(paymentId);
            if (payment is null)
            {
                return TypedResults.NotFound($"Payment {paymentId} not found");
            }

            if (payment.Status == PaymentStatus.Captured)
            {
                return TypedResults.Ok(ToResponse(payment));
            }

            if (payment.Status != PaymentStatus.Authorized)
            {
                return TypedResults.Conflict(new
                {
                    error = "Illegal state transition",
                    currentStatus = payment.Status.ToString(),
                });
            }

            await gateway.CaptureAsync(payment.ProviderReference!);

            await paymentStore.ExecuteAsync(() =>
            {
                payment.Capture(DateTime.UtcNow);
                return Task.CompletedTask;
            });

            metrics.RecordStatusChange(PaymentStatus.Captured);

            return TypedResults.Ok(ToResponse(payment));
        }).RequireAuthorization(AdminPolicy);

        routeBuilder.MapPost("/{paymentId:guid}/refund", async Task<IResult> (
            [FromServices] IPaymentStore paymentStore,
            [FromServices] IPaymentGateway gateway,
            [FromServices] PaymentMetrics metrics,
            Guid paymentId,
            [FromBody] RefundPaymentRequest? request) =>
        {
            var payment = await paymentStore.GetById(paymentId);
            if (payment is null)
            {
                return TypedResults.NotFound($"Payment {paymentId} not found");
            }

            if (payment.Status != PaymentStatus.Captured)
            {
                return TypedResults.Conflict(new
                {
                    error = "Illegal state transition",
                    currentStatus = payment.Status.ToString(),
                });
            }

            var refundAmount = request?.Amount ?? payment.Amount;

            await gateway.RefundAsync(payment.ProviderReference!, refundAmount);

            await paymentStore.ExecuteAsync(() =>
            {
                payment.Refund(refundAmount, DateTime.UtcNow);
                return Task.CompletedTask;
            });

            metrics.RecordStatusChange(PaymentStatus.Refunded);

            return TypedResults.Ok(ToResponse(payment));
        }).RequireAuthorization(AdminPolicy);
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
