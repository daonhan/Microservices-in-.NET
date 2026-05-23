namespace Payment.Service.Features.CapturePayment;

internal static class CapturePaymentEndpoint
{
    private const string AdminPolicy = "Administrator";

    public static IEndpointRouteBuilder MapCapturePayment(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{paymentId:guid}/capture", HandleAsync).RequireAuthorization(AdminPolicy);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        CapturePaymentHandler handler,
        Guid paymentId)
    {
        var result = await handler.HandleAsync(paymentId);
        return result.Outcome switch
        {
            CaptureOutcome.NotFound => TypedResults.NotFound($"Payment {paymentId} not found"),
            CaptureOutcome.Conflict => TypedResults.Conflict(new
            {
                error = "Illegal state transition",
                currentStatus = result.CurrentStatus!.Value.ToString(),
            }),
            _ => TypedResults.Ok(result.Response),
        };
    }
}
