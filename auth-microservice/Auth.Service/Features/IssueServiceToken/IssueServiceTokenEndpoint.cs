using Auth.Service.Domain;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Auth.Service.Features.IssueServiceToken;

internal static class IssueServiceTokenEndpoint
{
    private const string ClientCredentialsGrant = "client_credentials";

    public static IEndpointRouteBuilder MapIssueServiceToken(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/token", Handle).DisableAntiforgery();
        return routeBuilder;
    }

    internal static Results<Ok<AuthToken>, BadRequest<string>, UnauthorizedHttpResult> Handle(
        IssueServiceTokenHandler handler,
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

        var token = handler.Handle(clientId, clientSecret);

        return token is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(token);
    }
}
