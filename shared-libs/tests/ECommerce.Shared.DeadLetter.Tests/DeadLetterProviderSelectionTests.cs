using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ECommerce.Shared.Tests;

public sealed class DeadLetterProviderSelectionTests
{
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

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ImplementationType == typeof(RabbitMqDeadLetterCapture));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ImplementationType == typeof(RabbitMqDeadLetterPublisher));
    }

    [Fact]
    public void Given_RabbitMq_provider_When_AddDeadLetter_Then_Azure_dead_letter_implementations_are_not_registered()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = MessagingOptions.RabbitMqProvider,
        });

        var services = new ServiceCollection();
        services.AddDeadLetter(configuration);

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ImplementationType == typeof(AzureServiceBusDeadLetterCapture));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ImplementationType == typeof(AzureServiceBusDeadLetterPublisher));
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

    private static ConfigurationManager BuildConfig(Dictionary<string, string?> values)
    {
        var manager = new ConfigurationManager();
        manager.AddInMemoryCollection(values);
        return manager;
    }
}
