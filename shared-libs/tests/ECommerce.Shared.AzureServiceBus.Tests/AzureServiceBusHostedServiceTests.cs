using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ECommerce.Shared.Tests;

public sealed class AzureServiceBusHostedServiceTests
{
    [Fact]
    public async Task Given_handler_exhausts_retries_When_message_is_processed_Then_message_is_dead_lettered_with_normalized_metadata()
    {
        var handler = new FailingHandler();
        await using var service = BuildService(handler, retryCount: 2, queueName: "inventory-microservice");
        var correlationId = Guid.NewGuid();
        var message = NewReceivedMessage(new TestEvent("reserve") { CorrelationId = correlationId });
        var receiver = Substitute.For<ServiceBusReceiver>();
        IDictionary<string, object>? deadLetterProperties = null;

        receiver.CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        receiver.DeadLetterMessageAsync(
                Arg.Any<ServiceBusReceivedMessage>(),
                Arg.Do<IDictionary<string, object>>(properties => deadLetterProperties = properties),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await service.ProcessMessageAsync(new ProcessMessageEventArgs(message, receiver, CancellationToken.None));

        Assert.Equal(3, handler.Calls);
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await receiver.Received(1).DeadLetterMessageAsync(
            message,
            Arg.Any<IDictionary<string, object>>(),
            "HandlerFailed",
            Arg.Is<string>(description => description.Contains("InvalidOperationException", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());

        Assert.NotNull(deadLetterProperties);
        Assert.Equal("inventory-microservice", deadLetterProperties!["original_queue"]);
        Assert.Equal(nameof(TestEvent), deadLetterProperties["event_type"]);
        Assert.Equal("inventory-microservice", deadLetterProperties["service"]);
        Assert.Equal(3, deadLetterProperties["attempts"]);
        Assert.Equal(correlationId.ToString(), deadLetterProperties["correlation_id"]);
        Assert.Contains("InvalidOperationException", (string)deadLetterProperties["failure_reason"]);
        Assert.Contains(nameof(FailingHandler), (string)deadLetterProperties["stack_trace"]);
        Assert.True(DateTime.TryParse((string)deadLetterProperties["failed_at"], out _));
    }

    [Fact]
    public async Task Given_handler_succeeds_When_message_is_processed_Then_message_is_completed()
    {
        var handler = new RecordingHandler();
        await using var service = BuildService(handler, retryCount: 2, queueName: "order-microservice");
        var message = NewReceivedMessage(new TestEvent("created"));
        var receiver = Substitute.For<ServiceBusReceiver>();

        receiver.CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        receiver.DeadLetterMessageAsync(
                Arg.Any<ServiceBusReceivedMessage>(),
                Arg.Any<IDictionary<string, object>>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await service.ProcessMessageAsync(new ProcessMessageEventArgs(message, receiver, CancellationToken.None));

        Assert.Equal(1, handler.Calls);
        await receiver.Received(1).CompleteMessageAsync(message, Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(),
            Arg.Any<IDictionary<string, object>>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_unknown_subject_When_message_is_processed_Then_message_is_completed_without_dead_lettering()
    {
        var handler = new RecordingHandler();
        await using var service = BuildService(handler, retryCount: 2, queueName: "basket-microservice");
        var message = NewReceivedMessage(new TestEvent("ignored"), subject: "UnknownEvent");
        var receiver = Substitute.For<ServiceBusReceiver>();

        receiver.CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        receiver.DeadLetterMessageAsync(
                Arg.Any<ServiceBusReceivedMessage>(),
                Arg.Any<IDictionary<string, object>>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await service.ProcessMessageAsync(new ProcessMessageEventArgs(message, receiver, CancellationToken.None));

        Assert.Equal(0, handler.Calls);
        await receiver.Received(1).CompleteMessageAsync(message, Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(),
            Arg.Any<IDictionary<string, object>>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_handler_failure_has_large_exception_When_message_is_dead_lettered_Then_failure_metadata_is_bounded()
    {
        var handler = new FailingHandler(new string('x', 20_000));
        await using var service = BuildService(handler, retryCount: 0, queueName: "payment-microservice");
        var message = NewReceivedMessage(new TestEvent("authorize"));
        var receiver = Substitute.For<ServiceBusReceiver>();
        IDictionary<string, object>? deadLetterProperties = null;
        string? deadLetterDescription = null;

        receiver.DeadLetterMessageAsync(
                Arg.Any<ServiceBusReceivedMessage>(),
                Arg.Do<IDictionary<string, object>>(properties => deadLetterProperties = properties),
                Arg.Any<string>(),
                Arg.Do<string>(description => deadLetterDescription = description),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await service.ProcessMessageAsync(new ProcessMessageEventArgs(message, receiver, CancellationToken.None));

        Assert.NotNull(deadLetterProperties);
        Assert.True(((string)deadLetterProperties!["failure_reason"]).Length <= 1024);
        Assert.True(((string)deadLetterProperties["stack_trace"]).Length <= 16 * 1024);
        Assert.NotNull(deadLetterDescription);
        Assert.True(deadLetterDescription!.Length <= 4096);
    }

    private static AzureServiceBusHostedService BuildService(
        IEventHandler<TestEvent> handler,
        int retryCount,
        string queueName)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IEventHandler>(typeof(TestEvent), handler);
        var provider = services.BuildServiceProvider();
        var registrations = new EventHandlerRegistration();
        registrations.EventTypes[nameof(TestEvent)] = typeof(TestEvent);

        return new AzureServiceBusHostedService(
            provider,
            Substitute.For<ServiceBusClient>(),
            Options.Create(registrations),
            Options.Create(new EventBusOptions { QueueName = queueName, RetryCount = retryCount }),
            Options.Create(new AzureServiceBusOptions()),
            new AzureServiceBusTelemetry(),
            NullLogger<AzureServiceBusHostedService>.Instance);
    }

    private static ServiceBusReceivedMessage NewReceivedMessage(TestEvent @event, string subject = nameof(TestEvent)) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(@event)),
            messageId: @event.Id.ToString(),
            correlationId: @event.CorrelationId?.ToString() ?? string.Empty,
            subject: subject,
            contentType: "application/json",
            properties: new Dictionary<string, object>(),
            deliveryCount: 1);

    private sealed record TestEvent(string Payload) : Event;

    private sealed class FailingHandler(string message = "boom") : IEventHandler<TestEvent>
    {
        public int Calls { get; private set; }

        public Task Handle(TestEvent @event)
        {
            Calls++;
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingHandler : IEventHandler<TestEvent>
    {
        public int Calls { get; private set; }

        public Task Handle(TestEvent @event)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
