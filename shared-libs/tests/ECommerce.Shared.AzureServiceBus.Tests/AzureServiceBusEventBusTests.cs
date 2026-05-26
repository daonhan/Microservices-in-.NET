using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.EventBus;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ECommerce.Shared.Tests;

public sealed class AzureServiceBusEventBusTests
{
    private sealed record TestEvent(string Payload) : Event;

    [Fact]
    public async Task Given_event_When_PublishAsync_Then_sends_message_with_subject_and_json_body()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);

        var bus = new AzureServiceBusEventBus(
            client,
            Options.Create(new AzureServiceBusOptions { TopicName = "ecommerce-topic" }),
            new AzureServiceBusTelemetry());

        var @event = new TestEvent("hello");

        await bus.PublishAsync(@event);

        client.Received(1).CreateSender("ecommerce-topic");
        await sender.Received(1).SendMessageAsync(Arg.Is<ServiceBusMessage>(m =>
            m.Subject == nameof(TestEvent) &&
            m.ContentType == "application/json" &&
            m.MessageId == @event.Id.ToString()));
    }

    [Fact]
    public async Task Given_event_When_PublishAsync_Then_body_round_trips_to_original_payload()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);

        ServiceBusMessage? captured = null;
        await sender.SendMessageAsync(Arg.Do<ServiceBusMessage>(m => captured = m));

        var bus = new AzureServiceBusEventBus(
            client,
            Options.Create(new AzureServiceBusOptions()),
            new AzureServiceBusTelemetry());

        await bus.PublishAsync(new TestEvent("hello"));

        Assert.NotNull(captured);
        var deserialized = JsonSerializer.Deserialize<TestEvent>(captured!.Body.ToArray());
        Assert.Equal("hello", deserialized!.Payload);
    }
}
