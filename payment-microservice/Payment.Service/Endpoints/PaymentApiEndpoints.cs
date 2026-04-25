using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Payment.Service.Infrastructure.Data;

namespace Payment.Service.Endpoints;

public static class PaymentApiEndpoints
{
    private const string AdminRole = "Administrator";
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
}
