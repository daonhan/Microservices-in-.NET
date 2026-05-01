using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Shared.Infrastructure.Messaging;

/// <summary>
/// Provider-aware messaging registration. Reads <c>Messaging:Provider</c> at startup
/// and registers either the RabbitMQ adapter (default) or the Azure Service Bus adapter.
/// Event handlers and the <c>IEventBus</c> contract are unchanged across providers.
/// </summary>
public static class MessagingStartupExtensions
{
    public static IServiceCollection AddPlatformEventBus(this IServiceCollection services, IConfigurationManager configuration)
    {
        return ResolveProvider(configuration) switch
        {
            MessagingOptions.AzureServiceBusProvider => services.AddAzureServiceBusEventBus(configuration),
            _ => services.AddRabbitMqEventBus(configuration),
        };
    }

    public static IServiceCollection AddPlatformEventPublisher(this IServiceCollection services, IConfigurationManager configuration)
    {
        return ResolveProvider(configuration) switch
        {
            MessagingOptions.AzureServiceBusProvider => services.AddAzureServiceBusEventPublisher(configuration),
            _ => services.AddRabbitMqEventPublisher(configuration),
        };
    }

    public static IServiceCollection AddPlatformSubscriberService(this IServiceCollection services, IConfigurationManager configuration)
    {
        return ResolveProvider(configuration) switch
        {
            MessagingOptions.AzureServiceBusProvider => services.AddAzureServiceBusSubscriberService(configuration),
            _ => services.AddRabbitMqSubscriberService(configuration),
        };
    }

    private static string ResolveProvider(IConfiguration configuration)
    {
        var provider = configuration[$"{MessagingOptions.MessagingSectionName}:Provider"];
        return string.IsNullOrWhiteSpace(provider) ? MessagingOptions.RabbitMqProvider : provider;
    }
}
