using System.Security.Cryptography;
using Auth.Service.Domain;
using Auth.Service.Domain.Abstractions;
using Auth.Service.Services;
using Auth.Service.Services.Signing;
using ECommerce.Shared.Authentication;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace Auth.Tests;

public class LoginHandlerTests
{
    private const string DummyHash = "AQAAAAIAAYagAAAAEKiv9rLYG18wXY3D3K6RrWw65epqos2a30M1T6sBEdTj+G08XttZqsgurhQYE5QUdQ==";

    private static JwtTokenService BuildTokenService(RSA rsa)
    {
        var rsaKeyProvider = Substitute.For<IRsaKeyProvider>();
        rsaKeyProvider.GetActivePrivateKey().Returns(rsa);
        rsaKeyProvider.ActiveKeyId.Returns("test-kid");
        var options = new AuthOptions { AuthMicroserviceBaseAddress = "http://localhost" };
        return new JwtTokenService(rsaKeyProvider, options);
    }

    [Fact]
    public async Task HandleAsync_UnknownUsername_ReturnsNullAndInvokesDummyHashOnce()
    {
        // Arrange
        var authStore = Substitute.For<IAuthStore>();
        authStore.FindByUsernameAsync("ghost").Returns(Task.FromResult<User?>(null));
        var hasher = Substitute.For<IPasswordHasher<User>>();
        using var rsa = RSA.Create(2048);
        var handler = new LoginHandler(authStore, hasher, BuildTokenService(rsa));

        // Act
        var result = await handler.HandleAsync("ghost", "any-password");

        // Assert
        Assert.Null(result);
        hasher.Received(1).VerifyHashedPassword(
            Arg.Is<User>(u => u.Username == "" && u.PasswordHash == DummyHash && u.Role == ""),
            DummyHash,
            "any-password");
    }

    [Fact]
    public async Task HandleAsync_WrongPassword_ReturnsNull()
    {
        // Arrange
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Username = "alice",
            Role = "User",
            PasswordHash = hasher.HashPassword(
                new User { Username = "", Role = "", PasswordHash = "" }, "correct-password")
        };
        var authStore = Substitute.For<IAuthStore>();
        authStore.FindByUsernameAsync("alice").Returns(Task.FromResult<User?>(user));
        using var rsa = RSA.Create(2048);
        var handler = new LoginHandler(authStore, hasher, BuildTokenService(rsa));

        // Act
        var result = await handler.HandleAsync("alice", "wrong-password");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ReturnsAuthToken()
    {
        // Arrange
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Username = "alice",
            Role = "User",
            PasswordHash = hasher.HashPassword(
                new User { Username = "", Role = "", PasswordHash = "" }, "correct-password")
        };
        var authStore = Substitute.For<IAuthStore>();
        authStore.FindByUsernameAsync("alice").Returns(Task.FromResult<User?>(user));
        using var rsa = RSA.Create(2048);
        var handler = new LoginHandler(authStore, hasher, BuildTokenService(rsa));

        // Act
        var result = await handler.HandleAsync("alice", "correct-password");

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
    }
}
