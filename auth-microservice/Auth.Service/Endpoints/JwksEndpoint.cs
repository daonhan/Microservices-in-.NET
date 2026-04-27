using Auth.Service.Services.Signing;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Service.Endpoints;

public static class JwksEndpoint
{
    public static void RegisterJwksEndpoint(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/.well-known/jwks.json", GetJwks)
            .AllowAnonymous();
    }

    internal static JsonHttpResult<JwksDocument> GetJwks(
        IRsaKeyProvider keyProvider,
        MetricFactory metricFactory,
        HttpContext httpContext)
    {
        var keys = keyProvider.GetPublishedPublicKeys()
            .Select(BuildJwk)
            .ToArray();

        httpContext.Response.Headers.CacheControl = "public, max-age=300";
        metricFactory.Counter("jwks-served", "auth").Add(1);

        return TypedResults.Json(new JwksDocument(keys));
    }

    private static Jwk BuildJwk(PublishedKey published)
    {
        var parameters = published.PublicKey.ExportParameters(includePrivateParameters: false);

        return new Jwk(
            kty: "RSA",
            use: "sig",
            alg: SecurityAlgorithms.RsaSha256,
            kid: published.KeyId,
            n: Base64UrlEncoder.Encode(parameters.Modulus!),
            e: Base64UrlEncoder.Encode(parameters.Exponent!));
    }
}

public record JwksDocument(Jwk[] keys);

public record Jwk(string kty, string use, string alg, string kid, string n, string e);
