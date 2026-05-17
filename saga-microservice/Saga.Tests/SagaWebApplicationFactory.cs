using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Saga.Service.Infrastructure.Data.EntityFramework;

namespace Saga.Tests;

public class SagaWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SagaContext? _sagaContext;

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
        });
    }

    private static void RemoveHostedService<THostedService>(IServiceCollection services)
        where THostedService : IHostedService
    {
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
        _sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        _sagaContext.Database.Migrate();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new Task DisposeAsync()
    {
        if (_sagaContext is not null)
        {
            return _sagaContext.Database.EnsureDeletedAsync();
        }

        return Task.CompletedTask;
    }
}
