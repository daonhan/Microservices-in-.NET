using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Product.Service.Infrastructure.Data.EntityFramework;
using Product.Tests.Authentication;

namespace Product.Tests;

public class ProductWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private ProductContext? _productContext;

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
        _productContext = scope.ServiceProvider.GetRequiredService<ProductContext>();
        _productContext.Database.Migrate();
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
        if (_productContext is not null)
        {
            return _productContext.Database.EnsureDeletedAsync();
        }

        return Task.CompletedTask;
    }
}
