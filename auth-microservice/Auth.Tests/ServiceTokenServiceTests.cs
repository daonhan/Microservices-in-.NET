using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Auth.Service.Domain.Abstractions;
using Auth.Service.Domain.Tokens;
using Auth.Service.Models;
using Auth.Service.Services;
using ECommerce.Shared.Authentication;
using Microsoft.IdentityModel.Tokens;


using NSubstitute;

namespace Auth.Tests;

public class ServiceTokenServiceTests
{
    private static (ServiceTokenService Service, RSA Rsa) BuildService(params ServiceClient[] clients)
    {
        var options = new ServiceClientOptions { Clients = clients.ToList() };
        var rsaKeyProvider = Substitute.For<IRsaKeyProvider>();
        var rsa = RSA.Create(2048);
        rsaKeyProvider.GetActivePrivateKey().Returns(rsa);
        rsaKeyProvider.ActiveKeyId.Returns("test-kid");
        var authOptions = new AuthOptions { AuthMicroserviceBaseAddress = "http://localhost" };
        return (new ServiceTokenService(options, rsaKeyProvider, authOptions), rsa);
    }

    [Fact]
    public void Given_valid_client_credentials_When_generating_service_token_Then_returns_signed_jwt_with_service_role_and_subject()
    {
        // Arrange
        var (service, rsa) = BuildService(new ServiceClient { ClientId = "api-gateway", ClientSecret = "s3cret" });

        // Act
        var token = service.GenerateServiceToken("api-gateway", "s3cret");

        // Assert
        Assert.NotNull(token);

        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "http://localhost",
            ValidateAudience = false,
            ValidateLifetime = false,
            RequireSignedTokens = true,
            IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false)),
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
        };
        var principal = handler.ValidateToken(token!.Token, validationParameters, out var validated);

        Assert.NotNull(validated);
        var roleClaim = principal.FindFirst("user_role");
        Assert.NotNull(roleClaim);
        Assert.Equal("service", roleClaim.Value);

        var jwt = handler.ReadJwtToken(token.Token);
        Assert.Equal("api-gateway", jwt.Payload.Sub);

        rsa.Dispose();
    }

    [Fact]
    public void Given_unknown_client_id_When_generating_service_token_Then_returns_null()
    {
        var (service, rsa) = BuildService(new ServiceClient { ClientId = "api-gateway", ClientSecret = "s3cret" });

        var token = service.GenerateServiceToken("ghost", "s3cret");

        Assert.Null(token);
        rsa.Dispose();
    }

    [Fact]
    public void Given_wrong_client_secret_When_generating_service_token_Then_returns_null()
    {
        var (service, rsa) = BuildService(new ServiceClient { ClientId = "api-gateway", ClientSecret = "s3cret" });

        var token = service.GenerateServiceToken("api-gateway", "wrong");

        Assert.Null(token);
        rsa.Dispose();
    }

    [Fact]
    public void Given_no_configured_clients_When_generating_service_token_Then_returns_null()
    {
        var (service, rsa) = BuildService();

        var token = service.GenerateServiceToken("any", "any");

        Assert.Null(token);
        rsa.Dispose();
    }

    [Fact]
    public void Given_valid_credentials_When_generating_service_token_Then_token_signed_with_active_key_id()
    {
        var (service, rsa) = BuildService(new ServiceClient { ClientId = "api-gateway", ClientSecret = "s3cret" });

        var token = service.GenerateServiceToken("api-gateway", "s3cret");

        Assert.NotNull(token);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token!.Token);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.Equal("test-kid", jwt.Header.Kid);
        rsa.Dispose();
    }
}
