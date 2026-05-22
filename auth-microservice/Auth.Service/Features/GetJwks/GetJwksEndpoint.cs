using Microsoft.AspNetCore.Http.HttpResults;

namespace Auth.Service.Features.GetJwks;

internal static class GetJwksEndpoint
{
    public static IEndpointRouteBuilder MapGetJwks(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/.well-known/jwks.json", Handle).AllowAnonymous();
        return routeBuilder;
    }

    internal static JsonHttpResult<JwksDocument> Handle(GetJwksHandler handler, HttpContext httpContext)
        => TypedResults.Json(handler.Handle(httpContext));
}
