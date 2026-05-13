using ECommerce.Shared.Infrastructure.AzureServiceBus;
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

    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true;";

    private const string CloudConnectionString =
        "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==";

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
