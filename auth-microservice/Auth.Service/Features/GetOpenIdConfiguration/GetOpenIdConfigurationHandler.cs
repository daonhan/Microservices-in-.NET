using System.Diagnostics.CodeAnalysis;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Service.Features.GetOpenIdConfiguration;

internal sealed class GetOpenIdConfigurationHandler
{
    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Slice handlers are DI-resolved instance types, kept uniform across all four Auth feature slices.")]
    public OpenIdConfigurationDocument Handle(HttpContext httpContext)
    {
        var issuer = $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}";

        var document = new OpenIdConfigurationDocument(
            issuer: issuer,
            jwks_uri: $"{issuer}/.well-known/jwks.json",
            id_token_signing_alg_values_supported: new[] { SecurityAlgorithms.RsaSha256 });

        httpContext.Response.Headers.CacheControl = "public, max-age=300";
        return document;
    }
}
