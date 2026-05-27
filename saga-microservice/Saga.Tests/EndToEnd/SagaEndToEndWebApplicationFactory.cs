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
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Reaper;
using Saga.Tests.Authentication;

namespace Saga.Tests.EndToEnd;

public sealed class SagaEndToEndWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _sqlConnectionString;

    public SagaEndToEndWebApplicationFactory(string sqlConnectionString)
    {
        _sqlConnectionString = sqlConnectionString;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configurationBuilder =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _sqlConnectionString,
                // The RabbitMqHostedService is removed in ConfigureWebHost so no consumers run; we
                // drive handlers directly via DI.
                ["RabbitMq:HostName"] = "localhost",
                ["EventBus:QueueName"] = $"saga-end-to-end-{Guid.NewGuid():N}",
                ["Outbox:PublishIntervalInSeconds"] = "60",
                ["Saga:Reaper:IntervalInSeconds"] = "60",
                ["Authentication:AuthMicroserviceBaseAddress"] = "http://localhost:8003",
            });
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Tests");
        builder.ConfigureTestServices(services =>
        {
            RemoveHostedService<OutboxBackgroundService>(services);
            RemoveHostedService<RabbitMqHostedService>(services);
            RemoveHostedService<SagaReaperService>(services);
            ApplyMigrations(services);
            ConfigureTestAuthentication(services);
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

    private static void ApplyMigrations(IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        sagaContext.Database.Migrate();

        // OutboxContext is internal to the shared EventBus package, so resolve by Type and cast to DbContext.
        var outboxContextType = Type.GetType(
            "ECommerce.Shared.Infrastructure.Outbox.OutboxContext, ECommerce.Shared.EventBus",
            throwOnError: true)!;
        var outboxContext = (DbContext)scope.ServiceProvider.GetRequiredService(outboxContextType);
        outboxContext.Database.Migrate();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // The SQL container is torn down by the fixture, which is sufficient cleanup; we deliberately
    // do not call EnsureDeletedAsync here because EF migrations against a container that is about
    // to disappear race with disposal.
    public new Task DisposeAsync() => Task.CompletedTask;
}
