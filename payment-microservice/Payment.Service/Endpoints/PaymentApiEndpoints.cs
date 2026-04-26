using System.Security.Claims;
using System.Transactions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Infrastructure.Data;
using Payment.Service.Infrastructure.Gateways;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;
using Payment.Service.Observability;

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
            [FromServices] IOutboxStore outboxStore,
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

            await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                payment.Capture(DateTime.UtcNow);

                await paymentStore.SaveChangesAsync();

                await outboxStore.AddOutboxEvent(new PaymentCapturedEvent(
                    payment.PaymentId,
                    payment.OrderId,
                    payment.Amount));

                scope.Complete();
            });

            metrics.RecordStatusChange(PaymentStatus.Captured);

            return TypedResults.Ok(ToResponse(payment));
        }).RequireAuthorization(AdminPolicy);

        routeBuilder.MapPost("/{paymentId:guid}/refund", async Task<IResult> (
            [FromServices] IPaymentStore paymentStore,
            [FromServices] IOutboxStore outboxStore,
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

            await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                payment.Refund(DateTime.UtcNow);

                await paymentStore.SaveChangesAsync();

                await outboxStore.AddOutboxEvent(new PaymentRefundedEvent(
                    payment.PaymentId,
                    payment.OrderId,
                    refundAmount));

                scope.Complete();
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

    private static PaymentResponse ToResponse(Models.Payment payment)
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
