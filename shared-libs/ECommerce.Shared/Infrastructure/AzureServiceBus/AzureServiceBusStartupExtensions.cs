using Azure.Messaging.ServiceBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public static class AzureServiceBusStartupExtensions
{
    public static IServiceCollection AddAzureServiceBusEventBus(this IServiceCollection services, IConfigurationManager configuration)
    {
        services.AddAzureServiceBusTopologyDecision(configuration);
        services.AddSingleton<AzureServiceBusTelemetry>();

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value;
            return new ServiceBusClient(options.ConnectionString);
        });

        return services;
    }

    public static IServiceCollection AddAzureServiceBusEventPublisher(this IServiceCollection services, IConfigurationManager configuration)
    {
        services.AddAzureServiceBusTopologyDecision(configuration);
        services.AddAzureServiceBusTopologyProvisioner();
        services.Configure<EventBusOptions>(configuration.GetSection(EventBusOptions.EventBusSectionName));
        services.AddSingleton<IEventBus, AzureServiceBusEventBus>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AzureServiceBusTopicProvisioningHostedService>());

        return services;
    }

    public static IServiceCollection AddAzureServiceBusSubscriberService(this IServiceCollection services, IConfigurationManager configuration)
    {
        services.AddAzureServiceBusTopologyDecision(configuration);
        services.AddAzureServiceBusTopologyProvisioner();
        services.Configure<EventBusOptions>(configuration.GetSection(EventBusOptions.EventBusSectionName));

        services.AddSingleton<AzureServiceBusTelemetry>();
        services.AddHostedService<AzureServiceBusHostedService>();

        return services;
    }

    private static IServiceCollection AddAzureServiceBusTopologyProvisioner(this IServiceCollection services)
    {
        services.TryAddSingleton<IAzureServiceBusTopologyProvisioner, AzureServiceBusTopologyProvisioner>();
        return services;
    }

    private static IServiceCollection AddAzureServiceBusTopologyDecision(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AzureServiceBusOptions>()
            .Bind(configuration.GetSection(AzureServiceBusOptions.AzureServiceBusSectionName))
            .Validate(
                options => AzureServiceBusTopologyProvisioningPolicy.IsValidPolicy(options.AutoProvisionTopology),
                "AzureServiceBus:AutoProvisionTopology must be one of Auto, Always, Never.")
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AzureServiceBusTopologyProvisioningDecisionLogHostedService>());

        return services;
    }

    private sealed class AzureServiceBusTopologyProvisioningDecisionLogHostedService(
        IOptions<AzureServiceBusOptions> options,
        ILogger<AzureServiceBusTopologyProvisioningDecisionLogHostedService> logger) : IHostedService
    {
        private static readonly Action<ILogger, string, bool, bool, string, Exception?> LogDecision =
            LoggerMessage.Define<string, bool, bool, string>(
                LogLevel.Information,
                new EventId(1, "AzureServiceBusTopologyProvisioningDecision"),
                "Azure Service Bus topology provisioning decision: policy {Policy}; emulator connection string: {IsEmulator}; will run: {ShouldProvision}; reason: {Reason}");

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var decision = AzureServiceBusTopologyProvisioningPolicy.Evaluate(options.Value);
            LogDecision(logger, decision.Policy, decision.IsEmulator, decision.ShouldProvision, decision.Reason, null);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
