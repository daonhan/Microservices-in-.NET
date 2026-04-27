using Auth.Service.Infrastructure.Data.EntityFramework;
using Auth.Service.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Auth.Tests;

public class AuthContextVerifyUserLoginTests
{
    private static readonly string DummyHash = "AQAAAAIAAYagAAAAEKiv9rLYG18wXY3D3K6RrWw65epqos2a30M1T6sBEdTj+G08XttZqsgurhQYE5QUdQ==";
    private readonly DbContextOptions<AuthContext> _options;
    private readonly IPasswordHasher<User> _hasherMock;

    public AuthContextVerifyUserLoginTests()
    {
        _options = new DbContextOptionsBuilder<AuthContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _hasherMock = Substitute.For<IPasswordHasher<User>>();
    }

    private async Task<AuthContext> CreateContextAsync()
    {
        var context = new AuthContext(_options, _hasherMock);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    [Fact]
    public async Task VerifyUserLogin_CorrectCredentials_ReturnsUser()
    {
        // Arrange
        using var context = await CreateContextAsync();
        var password = "CorrectPassword";
        
        _hasherMock.VerifyHashedPassword(
            Arg.Any<User>(), 
            Arg.Any<string>(), 
            password)
            .Returns(PasswordVerificationResult.Success);

        // Act
        var result = await context.VerifyUserLogin("microservices@daonhan.com", password);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("microservices@daonhan.com", result.Username);
        _hasherMock.Received(1).VerifyHashedPassword(
            Arg.Is<User>(u => u.Username == "microservices@daonhan.com"),
            Arg.Is<string>(h => h != DummyHash),
            password);
    }

    [Fact]
    public async Task VerifyUserLogin_WrongPassword_ReturnsNull()
    {
        // Arrange
        using var context = await CreateContextAsync();
        var password = "WrongPassword";
        
        _hasherMock.VerifyHashedPassword(
            Arg.Any<User>(), 
            Arg.Any<string>(), 
            password)
            .Returns(PasswordVerificationResult.Failed);

        // Act
        var result = await context.VerifyUserLogin("microservices@daonhan.com", password);

        // Assert
        Assert.Null(result);
        _hasherMock.Received(1).VerifyHashedPassword(
            Arg.Is<User>(u => u.Username == "microservices@daonhan.com"),
            Arg.Any<string>(),
            password);
    }

    [Fact]
    public async Task VerifyUserLogin_UnknownUsername_ReturnsNullAndInvokesDummyHashOnce()
    {
        // Arrange
        using var context = await CreateContextAsync();
        var password = "AnyPassword";

        // Act
        var result = await context.VerifyUserLogin("unknown@daonhan.com", password);

        // Assert
        Assert.Null(result);
        _hasherMock.Received(1).VerifyHashedPassword(
            Arg.Is<User>(u => u.Username == "" && u.PasswordHash == DummyHash && u.Role == ""),
            DummyHash,
            password);
    }

    [Fact]
    public async Task VerifyUserLogin_SuccessRehashNeeded_ReturnsUser()
    {
        // Arrange
        using var context = await CreateContextAsync();
        var password = "CorrectPasswordNeedsRehash";
        
        _hasherMock.VerifyHashedPassword(
            Arg.Any<User>(), 
            Arg.Any<string>(), 
            password)
            .Returns(PasswordVerificationResult.SuccessRehashNeeded);

        // Act
        var result = await context.VerifyUserLogin("microservices@daonhan.com", password);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("microservices@daonhan.com", result.Username);
    }
}
