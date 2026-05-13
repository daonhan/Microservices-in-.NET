using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        var provider = ResolveProvider(configuration);
        services.AddMessagingProviderLog(provider);

        return provider switch
        {
            MessagingOptions.RabbitMqProvider => services.AddRabbitMqEventBus(configuration),
            MessagingOptions.AzureServiceBusProvider => services.AddAzureServiceBusEventBus(configuration),
            _ => throw new InvalidOperationException($"Unhandled messaging provider '{provider}'."),
        };
    }

    public static IServiceCollection AddPlatformEventPublisher(this IServiceCollection services, IConfigurationManager configuration)
    {
        var provider = ResolveProvider(configuration);
        services.AddMessagingProviderLog(provider);

        return provider switch
        {
            MessagingOptions.RabbitMqProvider => services.AddRabbitMqEventPublisher(configuration),
            MessagingOptions.AzureServiceBusProvider => services.AddAzureServiceBusEventPublisher(configuration),
            _ => throw new InvalidOperationException($"Unhandled messaging provider '{provider}'."),
        };
    }

    public static IServiceCollection AddPlatformSubscriberService(this IServiceCollection services, IConfigurationManager configuration)
    {
        var provider = ResolveProvider(configuration);
        services.AddMessagingProviderLog(provider);

        return provider switch
        {
            MessagingOptions.RabbitMqProvider => services.AddRabbitMqSubscriberService(configuration),
            MessagingOptions.AzureServiceBusProvider => services.AddAzureServiceBusSubscriberService(configuration),
            _ => throw new InvalidOperationException($"Unhandled messaging provider '{provider}'."),
        };
    }

    private static string ResolveProvider(IConfiguration configuration)
    {
        var provider = configuration[$"{MessagingOptions.MessagingSectionName}:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            return MessagingOptions.RabbitMqProvider;
        }

        if (string.Equals(provider, MessagingOptions.RabbitMqProvider, StringComparison.OrdinalIgnoreCase))
        {
            return MessagingOptions.RabbitMqProvider;
        }

        if (string.Equals(provider, MessagingOptions.AzureServiceBusProvider, StringComparison.OrdinalIgnoreCase))
        {
            return MessagingOptions.AzureServiceBusProvider;
        }

        throw new InvalidOperationException(
            $"Unknown messaging provider '{provider}'. Valid values: {MessagingOptions.RabbitMqProvider}, {MessagingOptions.AzureServiceBusProvider}.");
    }

    private static void AddMessagingProviderLog(this IServiceCollection services, string provider)
    {
        services.TryAddSingleton(_ => new MessagingProviderSelection(provider));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MessagingProviderLogHostedService>());
    }

    private sealed record MessagingProviderSelection(string Provider);

    private sealed class MessagingProviderLogHostedService(
        MessagingProviderSelection selection,
        ILogger<MessagingProviderLogHostedService> logger) : IHostedService
    {
        private static readonly Action<ILogger, string, Exception?> LogProviderSelected =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(1, "MessagingProviderSelected"),
                "Messaging provider selected: {Provider}");

        public Task StartAsync(CancellationToken cancellationToken)
        {
            LogProviderSelected(logger, selection.Provider, null);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
