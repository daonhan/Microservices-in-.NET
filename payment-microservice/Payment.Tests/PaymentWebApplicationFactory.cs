using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.RabbitMq;
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
            RemoveHostedService<OutboxBackgroundService>(services);
            RemoveHostedService<RabbitMqHostedService>(services);
            ApplyMigrations(services);
            ConfigureTestAuthentication(services);
        });
    }

    private static void RemoveHostedService<THostedService>(IServiceCollection services)
        where THostedService : IHostedService
    {
        // Some tests assert against the Outbox state directly. The poller
        // racing with assertions can mark events Sent and mask real failures.
        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(THostedService))
            .ToArray();

        foreach (var descriptor in descriptors)
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
