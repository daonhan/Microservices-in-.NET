using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

internal sealed class AzureServiceBusTopicProvisioningHostedService(
    IOptions<AzureServiceBusOptions> options,
    IAzureServiceBusTopologyProvisioner provisioner,
    ILogger<AzureServiceBusTopicProvisioningHostedService> logger) : IHostedService
{
    private static readonly Action<ILogger, string, Exception?> LogTopicCreated =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, "AzureServiceBusTopicCreated"),
            "Azure Service Bus topic '{TopicName}' created.");

    private static readonly Action<ILogger, string, Exception?> LogTopicAlreadyExists =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3, "AzureServiceBusTopicAlreadyExists"),
            "Azure Service Bus topic '{TopicName}' already exists.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var decision = AzureServiceBusTopologyProvisioningPolicy.Evaluate(options.Value);
        if (!decision.ShouldProvision)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Value.TopicName))
        {
            throw new InvalidOperationException("AzureServiceBus:TopicName is required for topology provisioning.");
        }

        var result = await provisioner
            .EnsureTopicAsync(options.Value.TopicName, cancellationToken)
            .ConfigureAwait(false);

        if (result == AzureServiceBusTopologyProvisioningResult.Created)
        {
            LogTopicCreated(logger, options.Value.TopicName, null);
            return;
        }

        LogTopicAlreadyExists(logger, options.Value.TopicName, null);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
