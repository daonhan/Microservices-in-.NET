using Auth.Service.Domain;
using Auth.Service.Infrastructure.Data.EntityFramework;
using ECommerce.Shared.Qa;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Auth.Tests.Qa;

public class AuthQaSeedTests
{
    [Fact]
    public async Task GivenAuthModelCreated_WhenReadingSeededUsers_ThenAdminAndQaCustomersExist()
    {
        var options = new DbContextOptionsBuilder<AuthContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new AuthContext(options, new PasswordHasher<User>());
        await context.Database.EnsureCreatedAsync();

        var users = await context.Users.OrderBy(u => u.Username).ToListAsync();

        Assert.Equal(4, users.Count);
        Assert.Contains(users, u => u.Username == "microservices@daonhan.com" && u.Role == "Administrator");
        Assert.Contains(users, u => u.Id == QaPersonas.CustomerHappyId && u.Username == QaPersonas.CustomerHappyEmail && u.Role == QaPersonas.CustomerRole);
        Assert.Contains(users, u => u.Id == QaPersonas.CustomerDeclineId && u.Username == QaPersonas.CustomerDeclineEmail && u.Role == QaPersonas.CustomerRole);
        Assert.Contains(users, u => u.Id == QaPersonas.CustomerCancelId && u.Username == QaPersonas.CustomerCancelEmail && u.Role == QaPersonas.CustomerRole);
    }

    [Theory]
    [InlineData(QaPersonas.CustomerHappyEmail)]
    [InlineData(QaPersonas.CustomerDeclineEmail)]
    [InlineData(QaPersonas.CustomerCancelEmail)]
    public async Task GivenQaCustomer_WhenVerifyingDocumentedPassword_ThenLoginSucceeds(string username)
    {
        var options = new DbContextOptionsBuilder<AuthContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new AuthContext(options, new PasswordHasher<User>());
        await context.Database.EnsureCreatedAsync();

        var user = await context.VerifyUserLogin(username, QaPersonas.CustomerPassword);

        Assert.NotNull(user);
        Assert.Equal(QaPersonas.CustomerRole, user!.Role);
    }
}
