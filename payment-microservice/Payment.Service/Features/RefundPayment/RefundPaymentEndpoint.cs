using Microsoft.AspNetCore.Mvc;

namespace Payment.Service.Features.RefundPayment;

internal static class RefundPaymentEndpoint
{
    private const string AdminPolicy = "Administrator";

    public static IEndpointRouteBuilder MapRefundPayment(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{paymentId:guid}/refund", HandleAsync).RequireAuthorization(AdminPolicy);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        RefundPaymentHandler handler,
        Guid paymentId,
        [FromBody] RefundPaymentRequest? request)
    {
        var result = await handler.HandleAsync(paymentId, request);
        return result.Outcome switch
        {
            RefundOutcome.NotFound => TypedResults.NotFound($"Payment {paymentId} not found"),
            RefundOutcome.Conflict => TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = result.CurrentStatus!.Value.ToString(),
            }),
            _ => TypedResults.Ok(result.Response),
        };
    }
}
