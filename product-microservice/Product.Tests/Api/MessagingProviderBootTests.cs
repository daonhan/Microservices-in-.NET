using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Product.Tests.Api;

public sealed class MessagingProviderBootTests
{
    [Fact]
    public void Given_default_provider_When_Product_host_boots_Then_RabbitMq_event_bus_resolves()
    {
        using var factory = new ProductMessagingProviderFactory([]);

        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        Assert.IsType<RabbitMqEventBus>(bus);
    }

    [Fact]
    public void Given_AzureServiceBus_provider_When_Product_host_boots_Then_Azure_event_bus_resolves()
    {
        using var factory = new ProductMessagingProviderFactory(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==",
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "product"
        });

        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        Assert.IsType<AzureServiceBusEventBus>(bus);
    }

    [Fact]
    public void Given_AzureServiceBus_emulator_provider_When_Product_host_boots_Then_Azure_event_bus_resolves()
    {
        using var factory = new ProductMessagingProviderFactory(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
            ["AzureServiceBus:AdministrationConnectionString"] = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
            ["AzureServiceBus:AutoProvisionTopology"] = AzureServiceBusOptions.AutoProvisionTopologyNever,
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "product"
        });

        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        Assert.IsType<AzureServiceBusEventBus>(bus);
    }

    private sealed class ProductMessagingProviderFactory(Dictionary<string, string?> settings)
        : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configurationBuilder =>
            {
                configurationBuilder.AddInMemoryCollection(settings);
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Outbox:PublishIntervalInSeconds"] = "3600"
                });
            });

            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Tests");
        }
    }
}
