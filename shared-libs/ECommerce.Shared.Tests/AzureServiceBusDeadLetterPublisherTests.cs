using Azure.Messaging.ServiceBus;
using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.DeadLetter;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ECommerce.Shared.Tests;

public sealed class AzureServiceBusDeadLetterPublisherTests
{
    [Fact]
    public void Given_replay_request_When_Publish_Then_sends_message_to_configured_topic()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);

        var publisher = new AzureServiceBusDeadLetterPublisher(
            client,
            Options.Create(new AzureServiceBusOptions { TopicName = "ecommerce-topic" }));

        publisher.Publish(NewRequest());

        client.Received(1).CreateSender("ecommerce-topic");
        sender.Received(1).SendMessageAsync(
            Arg.Any<ServiceBusMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Given_replay_request_When_Publish_Then_message_preserves_payload_subject_and_correlation()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);
        ServiceBusMessage? captured = null;
        sender.SendMessageAsync(
                Arg.Do<ServiceBusMessage>(message => captured = message),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var request = NewRequest();

        var newMessageId = new AzureServiceBusDeadLetterPublisher(
                client,
                Options.Create(new AzureServiceBusOptions { TopicName = "ecommerce-topic" }))
            .Publish(request);

        Assert.NotNull(captured);
        Assert.Equal(request.Payload, captured!.Body.ToString());
        Assert.Equal(request.EventType, captured.Subject);
        Assert.Equal("application/json", captured.ContentType);
        Assert.Equal(newMessageId.ToString(), captured.MessageId);
        Assert.Equal(request.CorrelationId!.Value.ToString(), captured.CorrelationId);
    }

    [Fact]
    public void Given_replay_request_without_correlation_When_Publish_Then_correlation_falls_back_to_failure_id()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);
        ServiceBusMessage? captured = null;
        sender.SendMessageAsync(
                Arg.Do<ServiceBusMessage>(message => captured = message),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var request = NewRequest() with { CorrelationId = null };

        new AzureServiceBusDeadLetterPublisher(
                client,
                Options.Create(new AzureServiceBusOptions { TopicName = "ecommerce-topic" }))
            .Publish(request);

        Assert.NotNull(captured);
        Assert.Equal(request.FailureId.ToString(), captured!.CorrelationId);
        Assert.Equal(
            request.FailureId.ToString(),
            Assert.IsType<string>(captured.ApplicationProperties["correlation_id"]));
    }

    [Fact]
    public void Given_replay_request_When_Publish_Then_message_includes_replay_metadata()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(sender);
        ServiceBusMessage? captured = null;
        sender.SendMessageAsync(
                Arg.Do<ServiceBusMessage>(message => captured = message),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var request = NewRequest();

        new AzureServiceBusDeadLetterPublisher(
                client,
                Options.Create(new AzureServiceBusOptions { TopicName = "ecommerce-topic" }))
            .Publish(request);

        Assert.NotNull(captured);
        Assert.Equal(
            request.EventType,
            Assert.IsType<string>(captured!.ApplicationProperties["event_type"]));
        Assert.Equal(
            request.OriginalQueue,
            Assert.IsType<string>(captured.ApplicationProperties["original_queue"]));
        Assert.Equal(
            request.FailureId.ToString(),
            Assert.IsType<string>(captured.ApplicationProperties[AzureServiceBusDeadLetterPublisher.ReplayedFromProperty]));
    }

    private static DeadLetterReplayRequest NewRequest() => new(
        OriginalQueue: "inventory-microservice",
        EventType: "StockReservedEvent",
        Payload: "{\"orderId\":\"order-1\"}",
        CorrelationId: Guid.NewGuid(),
        FailureId: Guid.NewGuid());
}
