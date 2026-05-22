using Auth.Service.Domain.Abstractions;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Service.Features.GetJwks;

internal sealed class GetJwksHandler
{
    private readonly IRsaKeyProvider _keyProvider;
    private readonly MetricFactory _metricFactory;

    public GetJwksHandler(IRsaKeyProvider keyProvider, MetricFactory metricFactory)
    {
        _keyProvider = keyProvider;
        _metricFactory = metricFactory;
    }

    public JwksDocument Handle(HttpContext httpContext)
    {
        var keys = _keyProvider.GetPublishedPublicKeys()
            .Select(BuildJwk)
            .ToArray();

        httpContext.Response.Headers.CacheControl = "public, max-age=300";
        _metricFactory.Counter("jwks-served", "auth").Add(1);

        return new JwksDocument(keys);
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
