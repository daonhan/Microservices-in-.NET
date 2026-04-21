using Auth.Service.ApiModels;
using Auth.Service.Services;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Auth.Service.Endpoints;

public static class AuthApiEndpoints
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/login", Login);
    }

    internal static async Task<Results<Ok<Models.AuthToken>, UnauthorizedHttpResult>> Login(
        ITokenService tokenService,
        MetricFactory metricFactory,
        LoginRequest loginRequest)
    {
        var loginResult = await tokenService.GenerateAuthenticationToken(loginRequest.Username,
            loginRequest.Password);

        if (loginResult is null)
        {
            metricFactory.Counter("login-failure", "logins").Add(1);
            return TypedResults.Unauthorized();
        }

        metricFactory.Counter("login-success", "logins").Add(1);
        return TypedResults.Ok(loginResult);
    }
}
