using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

internal sealed class AzureServiceBusSubscriptionProvisioningHostedService(
    IOptions<AzureServiceBusOptions> serviceBusOptions,
    IOptions<EventBusOptions> eventBusOptions,
    IAzureServiceBusTopologyProvisioner provisioner,
    ILogger<AzureServiceBusSubscriptionProvisioningHostedService> logger) : IHostedService
{
    private static readonly Action<ILogger, string, string, Exception?> LogSubscriptionCreated =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(4, "AzureServiceBusSubscriptionCreated"),
            "Azure Service Bus subscription '{TopicName}/{SubscriptionName}' created.");

    private static readonly Action<ILogger, string, string, Exception?> LogSubscriptionAlreadyExists =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(5, "AzureServiceBusSubscriptionAlreadyExists"),
            "Azure Service Bus subscription '{TopicName}/{SubscriptionName}' already exists.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var decision = AzureServiceBusTopologyProvisioningPolicy.Evaluate(serviceBusOptions.Value);
        if (!decision.ShouldProvision)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(serviceBusOptions.Value.TopicName))
        {
            throw new InvalidOperationException("AzureServiceBus:TopicName is required for topology provisioning.");
        }

        if (string.IsNullOrWhiteSpace(eventBusOptions.Value.QueueName))
        {
            throw new InvalidOperationException(
                "EventBus:QueueName is required for Azure Service Bus subscription provisioning.");
        }

        await provisioner
            .EnsureTopicAsync(serviceBusOptions.Value.TopicName, cancellationToken)
            .ConfigureAwait(false);

        var result = await provisioner
            .EnsureSubscriptionAsync(
                serviceBusOptions.Value.TopicName,
                eventBusOptions.Value.QueueName,
                cancellationToken)
            .ConfigureAwait(false);

        if (result == AzureServiceBusTopologyProvisioningResult.Created)
        {
            LogSubscriptionCreated(logger, serviceBusOptions.Value.TopicName, eventBusOptions.Value.QueueName, null);
            return;
        }

        LogSubscriptionAlreadyExists(logger, serviceBusOptions.Value.TopicName, eventBusOptions.Value.QueueName, null);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
