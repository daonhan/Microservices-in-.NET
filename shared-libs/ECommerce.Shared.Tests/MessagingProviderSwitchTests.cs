using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ECommerce.Shared.Tests;

public sealed class MessagingProviderSwitchTests
{
    [Fact]
    public void Given_no_provider_When_AddPlatformEventPublisher_Then_RabbitMq_is_used_as_default()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["RabbitMq:HostName"] = "localhost",
            ["EventBus:QueueName"] = "test-queue",
        });

        var services = new ServiceCollection();
        services.AddPlatformEventPublisher(configuration);

        var descriptor = Assert.Single(services, sd => sd.ServiceType == typeof(IEventBus));
        Assert.Equal(typeof(RabbitMqEventBus), descriptor.ImplementationType);
    }

    [Fact]
    public void Given_blank_provider_When_AddPlatformEventPublisher_Then_RabbitMq_is_used_as_default()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = " ",
            ["RabbitMq:HostName"] = "localhost",
            ["EventBus:QueueName"] = "test-queue",
        });

        var services = new ServiceCollection();
        services.AddPlatformEventPublisher(configuration);

        var descriptor = Assert.Single(services, sd => sd.ServiceType == typeof(IEventBus));
        Assert.Equal(typeof(RabbitMqEventBus), descriptor.ImplementationType);
    }

    [Fact]
    public void Given_AzureServiceBus_provider_When_AddPlatformEventPublisher_Then_Azure_adapter_is_registered()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==",
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "test-subscription",
        });

        var services = new ServiceCollection();
        services.AddPlatformEventBus(configuration);
        services.AddPlatformEventPublisher(configuration);

        var bus = services.BuildServiceProvider().GetRequiredService<IEventBus>();
        Assert.IsType<AzureServiceBusEventBus>(bus);
    }

    [Fact]
    public void Given_unknown_provider_When_AddPlatformEventPublisher_Then_InvalidOperationException_is_thrown()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Kafka",
        });

        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddPlatformEventPublisher(configuration));
        Assert.Contains("Kafka", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Given_AzureServiceBus_provider_When_AddDeadLetter_Then_RabbitMq_dead_letter_dependencies_are_not_registered()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
        });

        var services = new ServiceCollection();
        services.AddDeadLetter(configuration);

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IDeadLetterPublisher)
            && descriptor.ImplementationType == typeof(RabbitMqDeadLetterPublisher));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(RabbitMqDeadLetterCapture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData(MessagingOptions.RabbitMqProvider)]
    public void Given_RabbitMq_or_missing_provider_When_AddDeadLetter_Then_RabbitMq_dead_letter_services_are_registered(string? provider)
    {
        var values = new Dictionary<string, string?>();
        if (provider is not null)
        {
            values["Messaging:Provider"] = provider;
        }

        var services = new ServiceCollection();
        services.AddDeadLetter(BuildConfig(values));

        var publisher = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IDeadLetterPublisher));
        Assert.Equal(typeof(RabbitMqDeadLetterPublisher), publisher.ImplementationType);

        var capture = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IDeadLetterCapture));
        Assert.Equal(typeof(RabbitMqDeadLetterCapture), capture.ImplementationType);
    }

    [Fact]
    public void Given_AzureServiceBus_provider_When_AddDeadLetter_Then_Azure_dead_letter_placeholders_are_registered()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
        });

        var services = new ServiceCollection();
        services.AddDeadLetter(configuration);

        var publisher = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IDeadLetterPublisher));
        Assert.Equal(typeof(AzureServiceBusDeadLetterPublisher), publisher.ImplementationType);

        var capture = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IDeadLetterCapture));
        Assert.Equal(typeof(AzureServiceBusDeadLetterCapture), capture.ImplementationType);
    }

    [Fact]
    public void Given_unknown_provider_When_AddDeadLetter_Then_InvalidOperationException_is_thrown()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Kafka",
        });

        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddDeadLetter(configuration));
        Assert.Contains("Kafka", ex.Message, StringComparison.Ordinal);
        Assert.Contains(MessagingOptions.RabbitMqProvider, ex.Message, StringComparison.Ordinal);
        Assert.Contains(MessagingOptions.AzureServiceBusProvider, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_platform_messaging_registered_When_host_starts_Then_selected_provider_is_logged()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.AzureServiceBusProvider,
            ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=ZmFrZWtleQ==",
            ["AzureServiceBus:TopicName"] = "ecommerce-topic",
            ["EventBus:QueueName"] = "test-subscription",
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddPlatformEventPublisher(builder.Configuration);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        Assert.Contains(
            loggerProvider.Messages,
            message => message.Contains("Messaging provider selected: AzureServiceBus", StringComparison.Ordinal));
    }

    private static ConfigurationManager BuildConfig(Dictionary<string, string?> values)
    {
        var manager = new ConfigurationManager();
        manager.AddInMemoryCollection(values);
        return manager;
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

            public void Log<TState>(LogLevel logLevel,
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
