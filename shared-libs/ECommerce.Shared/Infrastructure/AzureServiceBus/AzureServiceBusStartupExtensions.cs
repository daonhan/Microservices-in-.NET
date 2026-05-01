using Azure.Messaging.ServiceBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public static class AzureServiceBusStartupExtensions
{
    public static IServiceCollection AddAzureServiceBusEventBus(this IServiceCollection services, IConfigurationManager configuration)
    {
        services.Configure<AzureServiceBusOptions>(configuration.GetSection(AzureServiceBusOptions.AzureServiceBusSectionName));
        services.AddSingleton<AzureServiceBusTelemetry>();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value;
            return new ServiceBusClient(options.ConnectionString);
        });

        return services;
    }

    public static IServiceCollection AddAzureServiceBusEventPublisher(this IServiceCollection services, IConfigurationManager configuration)
    {
        services.Configure<EventBusOptions>(configuration.GetSection(EventBusOptions.EventBusSectionName));
        services.AddSingleton<IEventBus, AzureServiceBusEventBus>();

        return services;
    }

    public static IServiceCollection AddAzureServiceBusSubscriberService(this IServiceCollection services, IConfigurationManager configuration)
    {
        services.Configure<EventBusOptions>(configuration.GetSection(EventBusOptions.EventBusSectionName));

        services.AddSingleton<AzureServiceBusTelemetry>();
        services.AddHostedService<AzureServiceBusHostedService>();

        return services;
    }
}
