using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Options;

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

internal enum AzureServiceBusTopologyProvisioningResult
{
    AlreadyExists,
    Created,
}

internal interface IAzureServiceBusTopologyProvisioner
{
    Task<AzureServiceBusTopologyProvisioningResult> EnsureTopicAsync(
        string topicName,
        CancellationToken cancellationToken);

    Task<AzureServiceBusTopologyProvisioningResult> EnsureSubscriptionAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken);
}

internal sealed class AzureServiceBusTopologyProvisioner(
    IOptions<AzureServiceBusOptions> options) : IAzureServiceBusTopologyProvisioner
{
    public async Task<AzureServiceBusTopologyProvisioningResult> EnsureTopicAsync(
        string topicName,
        CancellationToken cancellationToken)
    {
        var client = new ServiceBusAdministrationClient(ResolveAdministrationConnectionString(options.Value));

        var topicExists = await client.TopicExistsAsync(topicName, cancellationToken).ConfigureAwait(false);
        if (topicExists.Value)
        {
            return AzureServiceBusTopologyProvisioningResult.AlreadyExists;
        }

        try
        {
            await client.CreateTopicAsync(topicName, cancellationToken).ConfigureAwait(false);
            return AzureServiceBusTopologyProvisioningResult.Created;
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            return AzureServiceBusTopologyProvisioningResult.AlreadyExists;
        }
    }

    public async Task<AzureServiceBusTopologyProvisioningResult> EnsureSubscriptionAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        var client = new ServiceBusAdministrationClient(ResolveAdministrationConnectionString(options.Value));

        var subscriptionExists = await client
            .SubscriptionExistsAsync(topicName, subscriptionName, cancellationToken)
            .ConfigureAwait(false);
        if (subscriptionExists.Value)
        {
            return AzureServiceBusTopologyProvisioningResult.AlreadyExists;
        }

        try
        {
            await client.CreateSubscriptionAsync(topicName, subscriptionName, cancellationToken).ConfigureAwait(false);
            return AzureServiceBusTopologyProvisioningResult.Created;
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            return AzureServiceBusTopologyProvisioningResult.AlreadyExists;
        }
    }

    internal static string ResolveAdministrationConnectionString(AzureServiceBusOptions options) =>
        string.IsNullOrWhiteSpace(options.AdministrationConnectionString)
            ? options.ConnectionString
            : options.AdministrationConnectionString;
}
