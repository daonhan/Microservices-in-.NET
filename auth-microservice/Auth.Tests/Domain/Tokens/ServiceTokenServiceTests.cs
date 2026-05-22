using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Auth.Service.Domain.Abstractions;
using Auth.Service.Domain.Tokens;
using ECommerce.Shared.Authentication;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Auth.Tests.Domain.Tokens;

public class ServiceTokenServiceTests
{
    private static (ServiceTokenService Service, RSA Rsa) BuildService()
    {
        var rsaKeyProvider = Substitute.For<IRsaKeyProvider>();
        var rsa = RSA.Create(2048);
        rsaKeyProvider.GetActivePrivateKey().Returns(rsa);
        rsaKeyProvider.ActiveKeyId.Returns("test-kid");
        var authOptions = new AuthOptions { AuthMicroserviceBaseAddress = "http://localhost" };
        return (new ServiceTokenService(rsaKeyProvider, authOptions), rsa);
    }

    [Fact]
    public void Given_client_id_When_generating_service_token_Then_returns_signed_jwt_with_service_role_and_subject()
    {
        // Arrange
        var (service, rsa) = BuildService();

        // Act
        var token = service.GenerateServiceToken("api-gateway");

        // Assert
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
        var principal = handler.ValidateToken(token.Token, validationParameters, out var validated);

        Assert.NotNull(validated);
        var roleClaim = principal.FindFirst("user_role");
        Assert.NotNull(roleClaim);
        Assert.Equal("service", roleClaim.Value);

        var jwt = handler.ReadJwtToken(token.Token);
        Assert.Equal("api-gateway", jwt.Payload.Sub);

        rsa.Dispose();
    }

    [Fact]
    public void Given_client_id_When_generating_service_token_Then_token_signed_with_active_key_id()
    {
        var (service, rsa) = BuildService();

        var token = service.GenerateServiceToken("api-gateway");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.Equal("test-kid", jwt.Header.Kid);
        rsa.Dispose();
    }
}
