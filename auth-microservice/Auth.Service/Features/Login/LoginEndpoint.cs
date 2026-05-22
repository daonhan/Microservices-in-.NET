using Auth.Service.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Auth.Service.Features.Login;

internal static class LoginEndpoint
{
    public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/login", HandleAsync).AllowAnonymous();
        return routeBuilder;
    }

    internal static async Task<Results<Ok<AuthToken>, UnauthorizedHttpResult>> HandleAsync(
        LoginHandler handler,
        LoginRequest request)
    {
        var token = await handler.HandleAsync(request.Username, request.Password);

        return token is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(token);
    }
}
