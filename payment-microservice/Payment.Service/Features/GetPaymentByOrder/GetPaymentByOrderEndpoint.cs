using System.Security.Claims;

namespace Payment.Service.Features.GetPaymentByOrder;

internal static class GetPaymentByOrderEndpoint
{
    public static IEndpointRouteBuilder MapGetPaymentByOrder(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/by-order/{orderId:guid}", HandleAsync).RequireAuthorization();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        GetPaymentByOrderHandler handler,
        ClaimsPrincipal user,
        Guid orderId)
    {
        var response = await handler.HandleAsync(user, orderId);
        if (response is null)
        {
            return TypedResults.NotFound($"No payment found for order {orderId}");
        }

        return TypedResults.Ok(response);
    }
}
