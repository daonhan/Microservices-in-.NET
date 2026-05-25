using Microsoft.Extensions.Configuration;

namespace ECommerce.Shared.Infrastructure.Messaging;

internal static class MessagingProviderResolver
{
    public static string Resolve(IConfiguration configuration)
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
}
