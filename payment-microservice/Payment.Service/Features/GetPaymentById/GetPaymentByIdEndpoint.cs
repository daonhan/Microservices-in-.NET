using System.Security.Claims;

namespace Payment.Service.Features.GetPaymentById;

internal static class GetPaymentByIdEndpoint
{
    public static IEndpointRouteBuilder MapGetPaymentById(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{paymentId:guid}", HandleAsync).RequireAuthorization();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        GetPaymentByIdHandler handler,
        ClaimsPrincipal user,
        Guid paymentId)
    {
        var response = await handler.HandleAsync(user, paymentId);
        if (response is null)
        {
            return TypedResults.NotFound($"Payment {paymentId} not found");
        }

        return TypedResults.Ok(response);
    }
}
