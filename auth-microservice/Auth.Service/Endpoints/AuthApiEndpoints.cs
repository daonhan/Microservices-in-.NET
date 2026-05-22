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

    internal static async Task<Results<Ok<Domain.AuthToken>, UnauthorizedHttpResult>> Login(
        LoginHandler loginHandler,
        MetricFactory metricFactory,
        LoginRequest loginRequest)
    {
        var loginResult = await loginHandler.HandleAsync(loginRequest.Username,
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
