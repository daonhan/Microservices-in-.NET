using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Basket.Tests.IntegrationEvents;

public sealed class MessagingProviderBootTests
{
    [Fact]
    public void Given_default_provider_When_Basket_host_boots_Then_RabbitMq_subscriber_adapter_resolves()
    {
        using var factory = new BasketMessagingProviderFactory([]);

        _ = factory.Services;

        Assert.Contains(typeof(RabbitMqHostedService), factory.HostedServiceTypes);
        Assert.DoesNotContain(typeof(AzureServiceBusHostedService), factory.HostedServiceTypes);
    }

    [Fact]
    public void Given_AzureServiceBus_provider_When_Basket_host_boots_Then_Azure_subscriber_adapter_resolves()
    {
        using var factory = new BasketMessagingProviderFactory(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==",
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "basket-microservice"
        });

        _ = factory.Services;

        Assert.Contains(typeof(AzureServiceBusHostedService), factory.HostedServiceTypes);
        Assert.DoesNotContain(typeof(RabbitMqHostedService), factory.HostedServiceTypes);
    }

    private sealed class BasketMessagingProviderFactory(Dictionary<string, string?> settings)
        : WebApplicationFactory<Program>
    {
        public IReadOnlyCollection<Type> HostedServiceTypes { get; private set; } = [];

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configurationBuilder =>
            {
                configurationBuilder.AddInMemoryCollection(settings);
            });

            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Tests");
            builder.ConfigureServices(services =>
            {
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
    }
}
