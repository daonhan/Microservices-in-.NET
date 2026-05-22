using Auth.Service.Infrastructure.Data.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Auth.Tests.Infrastructure.Data.EntityFramework;

public class EfAuthStoreTests
{
    private static async Task<AuthContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<AuthContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AuthContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    [Fact]
    public async Task FindByUsernameAsync_UnknownUsername_ReturnsNull()
    {
        // Arrange
        await using var context = await CreateContextAsync();
        var store = new EfAuthStore(context);

        // Act
        var result = await store.FindByUsernameAsync("unknown@daonhan.com");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FindByUsernameAsync_SeededUser_ReturnsEntity()
    {
        // Arrange
        await using var context = await CreateContextAsync();
        var store = new EfAuthStore(context);

        // Act
        var result = await store.FindByUsernameAsync("microservices@daonhan.com");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("microservices@daonhan.com", result.Username);
    }
}
