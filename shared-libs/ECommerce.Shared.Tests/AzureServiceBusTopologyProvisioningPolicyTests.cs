using Azure.Messaging.ServiceBus.Administration;
using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Shared.Tests;

public sealed class AzureServiceBusTopologyProvisioningPolicyTests
{
    [Fact]
    public void Given_Auto_policy_and_emulator_connection_When_Evaluate_Then_provisioning_is_selected()
    {
        var options = new AzureServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true;",
            AutoProvisionTopology = "Auto",
        };

        var decision = AzureServiceBusTopologyProvisioningPolicy.Evaluate(options);

        Assert.True(decision.ShouldProvision);
        Assert.True(decision.IsEmulator);
        Assert.Equal("Auto", decision.Policy);
    }

    [Fact]
    public void Given_no_policy_When_Evaluate_Then_Auto_is_the_default()
    {
        var decision = AzureServiceBusTopologyProvisioningPolicy.Evaluate(new AzureServiceBusOptions
        {
            ConnectionString = EmulatorConnectionString,
        });

        Assert.True(decision.ShouldProvision);
        Assert.Equal("Auto", decision.Policy);
    }

    [Theory]
    [InlineData("Auto", CloudConnectionString, false, false)]
    [InlineData("Always", EmulatorConnectionString, true, true)]
    [InlineData("Always", CloudConnectionString, true, false)]
    [InlineData("Never", EmulatorConnectionString, false, true)]
    [InlineData("Never", CloudConnectionString, false, false)]
    public void Given_policy_and_connection_string_When_Evaluate_Then_decision_matrix_is_applied(
        string policy,
        string connectionString,
        bool shouldProvision,
        bool isEmulator)
    {
        var decision = AzureServiceBusTopologyProvisioningPolicy.Evaluate(new AzureServiceBusOptions
        {
            ConnectionString = connectionString,
            AutoProvisionTopology = policy,
        });

        Assert.Equal(shouldProvision, decision.ShouldProvision);
        Assert.Equal(isEmulator, decision.IsEmulator);
        Assert.Equal(policy, decision.Policy);
    }

    [Fact]
    public async Task Given_invalid_policy_When_Azure_Service_Bus_host_starts_Then_options_validation_fails()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = CloudConnectionString,
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["AzureServiceBus:AutoProvisionTopology"] = "Sometimes",
            ["EventBus:QueueName"] = "test-subscription",
        });

        builder.Services.AddPlatformEventBus(builder.Configuration);

        using var host = builder.Build();

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains("AzureServiceBus:AutoProvisionTopology", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Auto, Always, Never", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_Auto_policy_and_cloud_connection_When_Azure_Service_Bus_host_starts_Then_skip_decision_is_logged()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = CloudConnectionString,
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["AzureServiceBus:AutoProvisionTopology"] = "Auto",
            ["EventBus:QueueName"] = "test-subscription",
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddPlatformEventBus(builder.Configuration);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        Assert.Contains(
            loggerProvider.Messages,
            message => message.Contains("Azure Service Bus topology provisioning decision", StringComparison.Ordinal)
                && message.Contains("policy Auto", StringComparison.Ordinal)
                && message.Contains("emulator connection string: False", StringComparison.Ordinal)
                && message.Contains("will run: False", StringComparison.Ordinal)
                && message.Contains("non-emulator connection strings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Given_Auto_policy_and_emulator_connection_When_publisher_host_starts_Then_topic_is_ensured()
    {
        var provisioner = new RecordingTopologyProvisioner(AzureServiceBusTopologyProvisioningResult.Created);
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = EmulatorConnectionString,
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "test-subscription",
        });
        builder.Services.AddSingleton<IAzureServiceBusTopologyProvisioner>(provisioner);
        builder.Services.AddPlatformEventBus(builder.Configuration);
        builder.Services.AddPlatformEventPublisher(builder.Configuration);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        Assert.Equal(["ecommerce-topic"], provisioner.EnsuredTopics);
    }

    [Fact]
    public async Task Given_Auto_policy_and_cloud_connection_When_publisher_host_starts_Then_topic_is_not_ensured()
    {
        var provisioner = new RecordingTopologyProvisioner(AzureServiceBusTopologyProvisioningResult.Created);
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = CloudConnectionString,
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["AzureServiceBus:AutoProvisionTopology"] = "Auto",
            ["EventBus:QueueName"] = "test-subscription",
        });
        builder.Services.AddSingleton<IAzureServiceBusTopologyProvisioner>(provisioner);
        builder.Services.AddPlatformEventBus(builder.Configuration);
        builder.Services.AddPlatformEventPublisher(builder.Configuration);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        Assert.Empty(provisioner.EnsuredTopics);
    }

    [Fact]
    public async Task Given_topic_is_created_When_publisher_host_starts_Then_created_log_is_written()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = EmulatorConnectionString,
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "test-subscription",
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddSingleton<IAzureServiceBusTopologyProvisioner>(
            new RecordingTopologyProvisioner(AzureServiceBusTopologyProvisioningResult.Created));
        builder.Services.AddPlatformEventBus(builder.Configuration);
        builder.Services.AddPlatformEventPublisher(builder.Configuration);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        Assert.Contains(
            loggerProvider.Messages,
            message => message.Contains("Azure Service Bus topic 'ecommerce-topic' created", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Given_topic_already_exists_When_publisher_host_starts_Then_existing_log_is_written()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = EmulatorConnectionString,
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "test-subscription",
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddSingleton<IAzureServiceBusTopologyProvisioner>(
            new RecordingTopologyProvisioner(AzureServiceBusTopologyProvisioningResult.AlreadyExists));
        builder.Services.AddPlatformEventBus(builder.Configuration);
        builder.Services.AddPlatformEventPublisher(builder.Configuration);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        Assert.Contains(
            loggerProvider.Messages,
            message => message.Contains("Azure Service Bus topic 'ecommerce-topic' already exists", StringComparison.Ordinal));
    }

    [Fact]
    public void Given_administration_connection_string_When_resolving_admin_connection_Then_data_plane_connection_is_unchanged()
    {
        var options = new AzureServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true;",
            AdministrationConnectionString = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true;",
        };

        var administrationConnectionString = AzureServiceBusTopologyProvisioner.ResolveAdministrationConnectionString(options);

        Assert.Equal(options.AdministrationConnectionString, administrationConnectionString);
        Assert.Contains("localhost:5673", options.ConnectionString, StringComparison.Ordinal);
    }

    [AsbEmulatorFact]
    public async Task Given_ASB_emulator_When_publisher_host_starts_Then_topic_is_ensured_and_event_can_publish()
    {
        var connectionString = Environment.GetEnvironmentVariable("ASB_EMULATOR_CONNECTION_STRING")
            ?? "Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
        var administrationConnectionString = Environment.GetEnvironmentVariable("ASB_EMULATOR_ADMINISTRATION_CONNECTION_STRING")
            ?? "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = connectionString,
            ["AzureServiceBus:AdministrationConnectionString"] = administrationConnectionString,
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "test-subscription",
        });
        builder.Services.AddPlatformEventBus(builder.Configuration);
        builder.Services.AddPlatformEventPublisher(builder.Configuration);

        using var host = builder.Build();

        await host.StartAsync();
        var eventBus = host.Services.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(new AsbEmulatorSmokeEvent("publisher-startup"));
        await host.StopAsync();

        var administrationClient = new ServiceBusAdministrationClient(administrationConnectionString);
        var topicExists = await administrationClient.TopicExistsAsync("ecommerce-topic");
        Assert.True(topicExists.Value);
    }

    private sealed record AsbEmulatorSmokeEvent(string Payload) : Event;

    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true;";

    private const string CloudConnectionString =
        "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==";

    private sealed class RecordingTopologyProvisioner(
        AzureServiceBusTopologyProvisioningResult result) : IAzureServiceBusTopologyProvisioner
    {
        public List<string> EnsuredTopics { get; } = [];

        public Task<AzureServiceBusTopologyProvisioningResult> EnsureTopicAsync(
            string topicName,
            CancellationToken cancellationToken)
        {
            EnsuredTopics.Add(topicName);
            return Task.FromResult(result);
        }
    }

    private sealed class AsbEmulatorFactAttribute : FactAttribute
    {
        public AsbEmulatorFactAttribute()
        {
            if (!string.Equals(
                Environment.GetEnvironmentVariable("ASB_EMULATOR_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
            {
                Skip = "Set ASB_EMULATOR_TESTS=true to run against the local Azure Service Bus emulator.";
            }
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull =>
                NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
