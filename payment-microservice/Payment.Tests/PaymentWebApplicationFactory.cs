using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Payment.Service.Infrastructure.Data.EntityFramework;
using Payment.Tests.Authentication;

namespace Payment.Tests;

public class PaymentWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PaymentContext? _paymentContext;

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
            DisableOutboxBackgroundService(services);
            ApplyMigrations(services);
            ConfigureTestAuthentication(services);
        });
    }

    private static void DisableOutboxBackgroundService(IServiceCollection services)
    {
        // Tests assert against the Outbox state directly. The poller racing
        // with the assertion would mark events Sent and exclude them from
        // GetUnpublishedOutboxEvents, masking real failures.
        var hosted = services
            .Where(d => d.ImplementationType == typeof(OutboxBackgroundService))
            .ToList();
        foreach (var descriptor in hosted)
        {
            services.Remove(descriptor);
        }
    }

    private void ApplyMigrations(IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var scope = serviceProvider.CreateScope();
        _paymentContext = scope.ServiceProvider.GetRequiredService<PaymentContext>();
        _paymentContext.Database.Migrate();
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
        if (_paymentContext is not null)
        {
            return _paymentContext.Database.EnsureDeletedAsync();
        }

        return Task.CompletedTask;
    }
}
