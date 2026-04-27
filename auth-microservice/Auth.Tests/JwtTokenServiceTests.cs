using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Auth.Service.Infrastructure.Data;
using Auth.Service.Models;
using Auth.Service.Services;
using Auth.Service.Services.Signing;
using ECommerce.Shared.Authentication;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Auth.Tests;

public class JwtTokenServiceTests
{
    [Fact]
    public async Task GenerateAuthenticationToken_ProducesRs256TokenWithCorrectClaims()
    {
        // Arrange
        var authStore = Substitute.For<IAuthStore>();
        authStore.VerifyUserLogin("alice", "password")
            .Returns(Task.FromResult<User?>(new User { Username = "alice", Role = "User", PasswordHash = "hash" }));

        var rsaKeyProvider = Substitute.For<IRsaKeyProvider>();
        using var rsa = RSA.Create(2048);
        rsaKeyProvider.GetActivePrivateKey().Returns(rsa);
        rsaKeyProvider.ActiveKeyId.Returns("test-kid");

        var options = new AuthOptions { AuthMicroserviceBaseAddress = "http://localhost" };
        var service = new JwtTokenService(authStore, rsaKeyProvider, options);

        // Act
        var token = await service.GenerateAuthenticationToken("alice", "password");

        // Assert
        Assert.NotNull(token);
        
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token.Token);

        // Assert Header
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.Equal("test-kid", jwt.Header.Kid);

        // Assert Signature
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "http://localhost",
            ValidateAudience = false,
            ValidateLifetime = false,
            RequireSignedTokens = true,
            IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false))
        };
        var claimsPrincipal = handler.ValidateToken(token.Token, validationParameters, out var validatedToken);
        Assert.NotNull(validatedToken);

        // Assert Claims
        var nameClaim = claimsPrincipal.FindFirst(JwtRegisteredClaimNames.Name);
        Assert.NotNull(nameClaim);
        Assert.Equal("alice", nameClaim.Value);

        var roleClaim = claimsPrincipal.FindFirst("user_role");
        Assert.NotNull(roleClaim);
        Assert.Equal("User", roleClaim.Value);

        // Assert Expiry
        var expSeconds = (int)jwt.Payload.Expiration!.Value;
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var diff = expSeconds - nowSeconds;
        Assert.True(diff is >= 890 and <= 910, $"Expected exp - now ≈ 900s, got {diff}");
    }
}
