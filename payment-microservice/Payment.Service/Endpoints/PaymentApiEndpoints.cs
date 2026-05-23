using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Infrastructure.Observability;

namespace Payment.Service.Endpoints;

public static class PaymentApiEndpoints
{
    private const string AdminRole = "Administrator";
    private const string AdminPolicy = "Administrator";
    private const string CustomerIdClaim = "customerId";

    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/by-order/{orderId:guid}", async Task<IResult> (
            [FromServices] IPaymentStore paymentStore,
            ClaimsPrincipal user,
            Guid orderId) =>
        {
            var payment = await paymentStore.GetByOrder(orderId);
            if (payment is null)
            {
                return TypedResults.NotFound($"No payment found for order {orderId}");
            }

            if (!IsAuthorized(user, payment.CustomerId))
            {
                return TypedResults.NotFound($"No payment found for order {orderId}");
            }

            return TypedResults.Ok(ToResponse(payment));
        }).RequireAuthorization();

        routeBuilder.MapGet("/{paymentId:guid}", async Task<IResult> (
            [FromServices] IPaymentStore paymentStore,
            ClaimsPrincipal user,
            Guid paymentId) =>
        {
            var payment = await paymentStore.GetById(paymentId);
            if (payment is null)
            {
                return TypedResults.NotFound($"Payment {paymentId} not found");
            }

            if (!IsAuthorized(user, payment.CustomerId))
            {
                return TypedResults.NotFound($"Payment {paymentId} not found");
            }

            return TypedResults.Ok(ToResponse(payment));
        }).RequireAuthorization();

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

    private static bool IsAuthorized(ClaimsPrincipal user, string customerId)
    {
        if (user.HasClaim("user_role", AdminRole))
        {
            return true;
        }

        var callerCustomerId = user.FindFirst(CustomerIdClaim)?.Value;
        return callerCustomerId is not null && callerCustomerId == customerId;
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
