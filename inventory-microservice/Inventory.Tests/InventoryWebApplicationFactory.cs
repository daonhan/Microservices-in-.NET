using Inventory.Service.Infrastructure.Data.EntityFramework;
using Inventory.Tests.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Inventory.Tests;

public class InventoryWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private InventoryContext? _inventoryContext;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configurationBuilder =>
        {
            configurationBuilder.AddConfiguration(new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.Tests.json")
                .Build());
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            ApplyMigrations(services);
            ConfigureTestAuthentication(services);
        });
    }

    private void ApplyMigrations(IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var scope = serviceProvider.CreateScope();
        _inventoryContext = scope.ServiceProvider.GetRequiredService<InventoryContext>();
        _inventoryContext.Database.Migrate();
    }

    private static void ConfigureTestAuthentication(IServiceCollection services)
    {
        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
        });

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new Task DisposeAsync()
    {
        if (_inventoryContext is not null)
        {
            return _inventoryContext.Database.EnsureDeletedAsync();
        }

        return Task.CompletedTask;
    }
}
