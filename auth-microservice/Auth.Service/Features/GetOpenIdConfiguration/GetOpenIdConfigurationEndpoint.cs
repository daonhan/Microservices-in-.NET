using Microsoft.AspNetCore.Http.HttpResults;

namespace Auth.Service.Features.GetOpenIdConfiguration;

internal static class GetOpenIdConfigurationEndpoint
{
    public static IEndpointRouteBuilder MapGetOpenIdConfiguration(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/.well-known/openid-configuration", Handle).AllowAnonymous();
        return routeBuilder;
    }

    internal static JsonHttpResult<OpenIdConfigurationDocument> Handle(
        GetOpenIdConfigurationHandler handler, HttpContext httpContext)
        => TypedResults.Json(handler.Handle(httpContext));
}
