using System.Net;
using System.Net.Http.Json;
using Auth.Service.Features.GetOpenIdConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Tests.Features.GetOpenIdConfiguration;

public class GetOpenIdConfigurationEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly AuthWebApplicationFactory _factory;

    public GetOpenIdConfigurationEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetOpenIdConfiguration_WhenRequestedOverHttp_ThenReturnsJsonWithCacheControl()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://auth.daonhan.com")
        });

        var response = await client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
        var document = await response.Content.ReadFromJsonAsync<OpenIdConfigurationDocument>();
        Assert.NotNull(document);
        Assert.Equal("https://auth.daonhan.com", document!.issuer);
        Assert.Equal("https://auth.daonhan.com/.well-known/jwks.json", document.jwks_uri);
    }

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
