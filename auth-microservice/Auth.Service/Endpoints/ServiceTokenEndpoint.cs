using Auth.Service.Domain;
using Auth.Service.Services;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Auth.Service.Endpoints;

public static class ServiceTokenEndpoint
{
    private const string ClientCredentialsGrant = "client_credentials";

    public static void RegisterServiceTokenEndpoint(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/token", IssueToken).DisableAntiforgery();
    }

    internal static Results<Ok<AuthToken>, BadRequest<string>, UnauthorizedHttpResult> IssueToken(
        IServiceTokenService serviceTokenService,
        MetricFactory metricFactory,
        [FromForm(Name = "grant_type")] string? grantType,
        [FromForm(Name = "client_id")] string? clientId,
        [FromForm(Name = "client_secret")] string? clientSecret)
    {
        if (!string.Equals(grantType, ClientCredentialsGrant, StringComparison.Ordinal))
        {
            metricFactory.Counter("service-token-failure", "tokens").Add(1);
            return TypedResults.BadRequest("unsupported_grant_type");
        }

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            metricFactory.Counter("service-token-failure", "tokens").Add(1);
            return TypedResults.Unauthorized();
        }

        var token = serviceTokenService.GenerateServiceToken(clientId, clientSecret);

        if (token is null)
        {
            metricFactory.Counter("service-token-failure", "tokens").Add(1);
            return TypedResults.Unauthorized();
        }

        metricFactory.Counter("service-token-success", "tokens").Add(1);
        return TypedResults.Ok(token);
    }
}
