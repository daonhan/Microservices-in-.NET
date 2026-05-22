using Auth.Service.Features.GetOpenIdConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Tests.Features.GetOpenIdConfiguration;

public class GetOpenIdConfigurationEndpointTests
{
    [Fact]
    public void GetOpenIdConfiguration_WhenInvoked_ThenReturnsDiscoveryDocumentWithCacheControl()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("auth.daonhan.com");

        // Act
        var result = GetOpenIdConfigurationEndpoint.Handle(
            new GetOpenIdConfigurationHandler(), httpContext);

        // Assert
        Assert.Equal("public, max-age=300", httpContext.Response.Headers.CacheControl.ToString());
        Assert.NotNull(result.Value);
        Assert.Equal("https://auth.daonhan.com", result.Value!.issuer);
        Assert.Equal("https://auth.daonhan.com/.well-known/jwks.json", result.Value.jwks_uri);
        Assert.Equal(
            new[] { SecurityAlgorithms.RsaSha256 },
            result.Value.id_token_signing_alg_values_supported);
    }
}
