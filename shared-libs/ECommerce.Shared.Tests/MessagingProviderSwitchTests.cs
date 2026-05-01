using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Shared.Tests;

public sealed class MessagingProviderSwitchTests
{
    [Fact]
    public void Given_no_provider_When_AddPlatformEventPublisher_Then_RabbitMq_is_used_as_default()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["RabbitMq:HostName"] = "localhost",
            ["EventBus:QueueName"] = "test-queue",
        });

        var services = new ServiceCollection();
        services.AddPlatformEventBus(configuration);
        services.AddPlatformEventPublisher(configuration);

        var bus = services.BuildServiceProvider().GetRequiredService<IEventBus>();
        Assert.IsType<RabbitMqEventBus>(bus);
    }

    [Fact]
    public void Given_AzureServiceBus_provider_When_AddPlatformEventPublisher_Then_Azure_adapter_is_registered()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==",
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "test-subscription",
        });

        var services = new ServiceCollection();
        services.AddPlatformEventBus(configuration);
        services.AddPlatformEventPublisher(configuration);

        var bus = services.BuildServiceProvider().GetRequiredService<IEventBus>();
        Assert.IsType<ECommerce.Shared.Infrastructure.AzureServiceBus.AzureServiceBusEventBus>(bus);
    }

    private static ConfigurationManager BuildConfig(Dictionary<string, string?> values)
    {
        var manager = new ConfigurationManager();
        manager.AddInMemoryCollection(values);
        return manager;
    }
}
