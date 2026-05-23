using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Order.Tests.IntegrationEvents;

public sealed class MessagingProviderBootTests
{
    [Fact]
    public void Given_default_provider_When_Order_host_boots_Then_RabbitMq_messaging_adapters_resolve()
    {
        using var factory = new OrderMessagingProviderFactory([]);

        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        Assert.IsType<RabbitMqEventBus>(bus);
        Assert.Contains(typeof(RabbitMqHostedService), factory.HostedServiceTypes);
        Assert.DoesNotContain(typeof(AzureServiceBusHostedService), factory.HostedServiceTypes);
    }

    [Fact]
    public void Given_AzureServiceBus_provider_When_Order_host_boots_Then_Azure_messaging_adapters_resolve()
    {
        using var factory = new OrderMessagingProviderFactory(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==",
            ["AzureServiceBus:TopicName"] = "ecommerce-topic"
        });

        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var eventBusOptions = scope.ServiceProvider.GetRequiredService<IOptions<EventBusOptions>>().Value;

        Assert.IsType<AzureServiceBusEventBus>(bus);
        Assert.Equal("order-microservice", eventBusOptions.QueueName);
        Assert.Contains(typeof(AzureServiceBusHostedService), factory.HostedServiceTypes);
        Assert.DoesNotContain(typeof(RabbitMqHostedService), factory.HostedServiceTypes);
    }

    private sealed class OrderMessagingProviderFactory(Dictionary<string, string?> settings)
        : WebApplicationFactory<Program>
    {
        public IReadOnlyCollection<Type> HostedServiceTypes { get; private set; } = [];

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configurationBuilder =>
            {
                configurationBuilder.AddInMemoryCollection(settings);
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Outbox:PublishIntervalInSeconds"] = "3600",
                    ["RabbitMq:HostName"] = "host.docker.internal",
                    ["Redis:Configuration"] = "host.docker.internal:6379"
                });
            });

            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Tests");
            builder.ConfigureServices(services =>
            {
                if (UsesRabbitMqProvider())
                {
                    services.RemoveAll<IRabbitMqConnection>();
                    services.AddSingleton<IRabbitMqConnection, UnusedRabbitMqConnection>();
                }

                HostedServiceTypes = services
                    .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
                    .Select(GetHostedServiceType)
                    .OfType<Type>()
                    .ToArray();

                services.RemoveAll<IHostedService>();
            });
        }

        private static Type? GetHostedServiceType(ServiceDescriptor descriptor) =>
            descriptor.ImplementationType
            ?? descriptor.ImplementationInstance?.GetType();

        private bool UsesRabbitMqProvider() =>
            !settings.TryGetValue("Messaging:Provider", out var provider)
            || !string.Equals(provider, MessagingOptions.AzureServiceBusProvider, StringComparison.OrdinalIgnoreCase);

        private sealed class UnusedRabbitMqConnection : IRabbitMqConnection
        {
            public IConnection Connection => throw new NotSupportedException(
                "Messaging provider boot tests verify DI wiring and must not publish messages.");
        }
    }
}
