using ApiGateway.Operator;
using ECommerce.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;

namespace ApiGateway.Tests.Operator;

public sealed class OperatorMessagingProviderTests
{
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

        OperatorModule.AddServices(builder);

        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType.FullName == "Azure.Messaging.ServiceBus.ServiceBusClient");
        Assert.DoesNotContain(builder.Services, descriptor =>
            descriptor.ServiceType.FullName == "ECommerce.Shared.Infrastructure.RabbitMq.IRabbitMqConnection");
    }
}
