using ECommerce.Shared.Authentication;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGateway.Tests.Composition;

public sealed class OperatorMessagingProviderTests
{
    [Fact]
    public void Given_gateway_appsettings_When_ASB_dead_letter_capture_is_configured_Then_subscriber_queue_names_are_declared()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(GatewayAppSettingsPath)
            .Build();

        var subscriptions = configuration
            .GetSection("AzureServiceBus:DeadLetterCaptureSubscriptions")
            .Get<string[]>() ?? [];

        Assert.Equal(
            [
                "basket-microservice",
                "order-microservice",
                "inventory-microservice",
                "payment-microservice",
                "shipping-microservice"
            ],
            subscriptions);
    }

    [Fact]
    public void Given_AzureServiceBus_messaging_provider_When_operator_services_are_added_Then_Azure_bus_infrastructure_is_registered()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==",
            ["AzureServiceBus:TopicName"] = "ecommerce-topic"
        });

        AddOperatorServices(builder);

        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType.FullName == "Azure.Messaging.ServiceBus.ServiceBusClient");
        Assert.DoesNotContain(builder.Services, descriptor =>
            descriptor.ServiceType.FullName == "ECommerce.Shared.Infrastructure.RabbitMq.IRabbitMqConnection");
    }

    [Fact]
    public void Given_RabbitMq_messaging_provider_When_operator_services_are_added_Then_broker_connection_is_lazy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.RabbitMqProvider,
            ["RabbitMq:HostName"] = "definitely-not-resolvable.invalid"
        });

        AddOperatorServices(builder);

        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType.FullName == "ECommerce.Shared.Infrastructure.RabbitMq.IRabbitMqConnection");
    }

    [Fact]
    public void Given_AzureServiceBus_messaging_provider_When_operator_services_are_added_Then_dead_letter_services_are_provider_selected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==",
            ["AzureServiceBus:TopicName"] = "ecommerce-topic"
        });

        AddOperatorServices(builder);

        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType.FullName == "ECommerce.Shared.Infrastructure.DeadLetter.IDeadLetterCapture"
            && descriptor.ImplementationType?.FullName == "ECommerce.Shared.Infrastructure.DeadLetter.AzureServiceBusDeadLetterCapture");
        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType.FullName == "ECommerce.Shared.Infrastructure.DeadLetter.IDeadLetterPublisher"
            && descriptor.ImplementationType?.FullName == "ECommerce.Shared.Infrastructure.DeadLetter.AzureServiceBusDeadLetterPublisher");
        Assert.DoesNotContain(builder.Services, descriptor =>
            descriptor.ImplementationType?.FullName == "ECommerce.Shared.Infrastructure.DeadLetter.RabbitMqDeadLetterCapture");
        Assert.DoesNotContain(builder.Services, descriptor =>
            descriptor.ImplementationType?.FullName == "ECommerce.Shared.Infrastructure.DeadLetter.RabbitMqDeadLetterPublisher");

        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
        });
    }

    private static string GatewayAppSettingsPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ApiGateway", "appsettings.json"));

    private static void AddOperatorServices(WebApplicationBuilder builder)
    {
        builder.Services.AddPlatformEventBus(builder.Configuration);
        builder.Services.AddDeadLetter(builder.Configuration);
        builder.Services.AddRequireOperatorPolicy();
    }
}
