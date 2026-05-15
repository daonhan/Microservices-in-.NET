using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using ECommerce.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ECommerce.Shared.Tests;

public sealed class AzureServiceBusDeadLetterCaptureTests
{
    [Fact]
    public void Given_dead_lettered_message_with_full_metadata_When_BuildMessage_Then_all_fields_are_normalized()
    {
        var correlationId = Guid.NewGuid();
        var failedAt = new DateTime(2026, 5, 14, 1, 2, 3, DateTimeKind.Utc);
        var properties = new Dictionary<string, object>
        {
            [AzureServiceBusHostedService.OriginalQueueProperty] = "inventory-microservice",
            [AzureServiceBusHostedService.EventTypeProperty] = "StockReserved",
            [AzureServiceBusHostedService.ServiceProperty] = "inventory-microservice",
            [AzureServiceBusHostedService.FailureReasonProperty] = "System.InvalidOperationException: boom",
            [AzureServiceBusHostedService.StackTraceProperty] = "at FailingHandler.Handle()",
            [AzureServiceBusHostedService.AttemptsProperty] = 4,
            [AzureServiceBusHostedService.FailedAtProperty] = failedAt.ToString("O"),
            [AzureServiceBusHostedService.CorrelationIdProperty] = correlationId.ToString(),
        };
        var payload = """{"Payload":"reserve"}""";
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromBytes(Encoding.UTF8.GetBytes(payload)),
            messageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            subject: "StockReserved",
            properties: properties,
            deliveryCount: 7);

        var dl = AzureServiceBusDeadLetterCapture.BuildMessage(message);

        Assert.Equal("StockReserved", dl.EventType);
        Assert.Equal("StockReserved", dl.RoutingKey);
        Assert.Equal("inventory-microservice", dl.OriginalQueue);
        Assert.Equal("inventory-microservice", dl.Service);
        Assert.Equal(payload, dl.Payload);
        Assert.Equal("System.InvalidOperationException: boom", dl.FailureReason);
        Assert.Equal("at FailingHandler.Handle()", dl.StackTrace);
        Assert.Equal(4, dl.Attempts);
        Assert.Equal(failedAt, dl.FailedAt);
        Assert.Equal(correlationId, dl.CorrelationId);
        Assert.Equal(DeadLetterOrigin.DeadLetter, dl.Origin);
    }

    [Fact]
    public void Given_message_missing_optional_metadata_When_BuildMessage_Then_safe_fallbacks_are_used()
    {
        var beforeBuild = DateTime.UtcNow.AddSeconds(-1);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            subject: "OrderConfirmed",
            properties: new Dictionary<string, object>(),
            deliveryCount: 3);

        var dl = AzureServiceBusDeadLetterCapture.BuildMessage(message);

        Assert.Equal("OrderConfirmed", dl.EventType);
        Assert.Equal(string.Empty, dl.OriginalQueue);
        Assert.Equal(string.Empty, dl.Service);
        Assert.Equal(3, dl.Attempts);
        Assert.True(dl.FailedAt >= beforeBuild, $"FailedAt {dl.FailedAt:o} should default to capture time, not before {beforeBuild:o}.");
        Assert.NotEqual(Guid.Empty, dl.CorrelationId);
    }

    [Fact]
    public void Given_message_missing_subject_and_event_type_property_When_BuildMessage_Then_event_type_falls_back_to_unknown()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            properties: new Dictionary<string, object>(),
            deliveryCount: 0);

        var dl = AzureServiceBusDeadLetterCapture.BuildMessage(message);

        Assert.Equal("unknown", dl.EventType);
        Assert.Equal(1, dl.Attempts);
    }

    [Fact]
    public void Given_correlation_id_only_on_broker_message_When_BuildMessage_Then_correlation_id_is_read_from_message()
    {
        var brokerCorrelation = Guid.NewGuid();
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            correlationId: brokerCorrelation.ToString(),
            subject: "PaymentAuthorized",
            properties: new Dictionary<string, object>(),
            deliveryCount: 1);

        var dl = AzureServiceBusDeadLetterCapture.BuildMessage(message);

        Assert.Equal(brokerCorrelation, dl.CorrelationId);
    }

    [Fact]
    public async Task Given_store_capture_succeeds_When_OnMessageReceived_Then_message_is_completed_and_metric_emitted()
    {
        var store = new RecordingStore();
        await using var capture = BuildCapture(store);
        var receiver = Substitute.For<ServiceBusReceiver>();
        var message = NewMessage("inventory-microservice", "StockReserved");

        var capturedTags = StartListening(out var observed);

        var args = new ProcessMessageEventArgs(message, receiver, CancellationToken.None);
        await capture.OnMessageReceivedAsync(args);

        await receiver.Received(1).CompleteMessageAsync(message, Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().AbandonMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(),
            Arg.Any<IDictionary<string, object>>(),
            Arg.Any<CancellationToken>());
        Assert.Single(store.Captured);
        Assert.True(observed.Wait(TimeSpan.FromSeconds(2)), "Expected dlq_messages_total measurement.");

        Assert.Contains(capturedTags, t => t.Key == "provider" && (string?)t.Value == MessagingOptions.AzureServiceBusProvider);
        Assert.Contains(capturedTags, t => t.Key == "service" && (string?)t.Value == "inventory-microservice");
        Assert.Contains(capturedTags, t => t.Key == "event_type" && (string?)t.Value == "StockReserved");
    }

    [Fact]
    public async Task Given_store_capture_throws_When_OnMessageReceived_Then_message_is_abandoned_and_not_completed()
    {
        var store = new RecordingStore { ThrowOnCapture = true };
        await using var capture = BuildCapture(store);
        var receiver = Substitute.For<ServiceBusReceiver>();
        var message = NewMessage("payment-microservice", "PaymentAuthorized");

        var args = new ProcessMessageEventArgs(message, receiver, CancellationToken.None);
        await capture.OnMessageReceivedAsync(args);

        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await receiver.Received(1).AbandonMessageAsync(message, Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_capture_subscriptions_are_empty_When_StartAsync_Then_no_processor_is_created_and_host_stays_alive()
    {
        var client = Substitute.For<ServiceBusClient>();
        var capture = new AzureServiceBusDeadLetterCapture(
            new ServiceCollection().BuildServiceProvider(),
            client,
            Options.Create(new AzureServiceBusOptions { DeadLetterCaptureSubscriptions = new List<string>() }),
            NullLogger<AzureServiceBusDeadLetterCapture>.Instance);

        await capture.StartAsync(CancellationToken.None);
        await capture.StopAsync(CancellationToken.None);
        await capture.DisposeAsync();

        client.DidNotReceiveWithAnyArgs().CreateProcessor(default!, default!, default!);
    }

    [Fact]
    public async Task Given_capture_subscriptions_contain_only_blanks_When_StartAsync_Then_no_processor_is_created()
    {
        var client = Substitute.For<ServiceBusClient>();
        var capture = new AzureServiceBusDeadLetterCapture(
            new ServiceCollection().BuildServiceProvider(),
            client,
            Options.Create(new AzureServiceBusOptions
            {
                DeadLetterCaptureSubscriptions = new List<string> { string.Empty, " ", "\t" },
            }),
            NullLogger<AzureServiceBusDeadLetterCapture>.Instance);

        await capture.StartAsync(CancellationToken.None);
        await capture.StopAsync(CancellationToken.None);
        await capture.DisposeAsync();

        client.DidNotReceiveWithAnyArgs().CreateProcessor(default!, default!, default!);
    }

    [Fact]
    public async Task Given_multiple_subscriptions_When_StartAsync_Then_one_processor_per_subscription_is_started()
    {
        var client = Substitute.For<ServiceBusClient>();
        var p1 = Substitute.For<ServiceBusProcessor>();
        var p2 = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("ecommerce-topic", "inventory-microservice", Arg.Any<ServiceBusProcessorOptions>()).Returns(p1);
        client.CreateProcessor("ecommerce-topic", "payment-microservice", Arg.Any<ServiceBusProcessorOptions>()).Returns(p2);

        var capture = new AzureServiceBusDeadLetterCapture(
            new ServiceCollection().BuildServiceProvider(),
            client,
            Options.Create(new AzureServiceBusOptions
            {
                TopicName = "ecommerce-topic",
                DeadLetterCaptureSubscriptions = new List<string> { "inventory-microservice", "payment-microservice" },
            }),
            NullLogger<AzureServiceBusDeadLetterCapture>.Instance);

        await capture.StartAsync(CancellationToken.None);

        await p1.Received(1).StartProcessingAsync(Arg.Any<CancellationToken>());
        await p2.Received(1).StartProcessingAsync(Arg.Any<CancellationToken>());

        await capture.StopAsync(CancellationToken.None);
        await p1.Received(1).StopProcessingAsync(Arg.Any<CancellationToken>());
        await p2.Received(1).StopProcessingAsync(Arg.Any<CancellationToken>());

        await capture.DisposeAsync();
    }

    [Fact]
    public async Task Given_duplicate_subscriptions_When_StartAsync_Then_only_one_processor_per_subscription_is_started()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("ecommerce-topic", "inventory-microservice", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        var capture = new AzureServiceBusDeadLetterCapture(
            new ServiceCollection().BuildServiceProvider(),
            client,
            Options.Create(new AzureServiceBusOptions
            {
                TopicName = "ecommerce-topic",
                DeadLetterCaptureSubscriptions = new List<string> { "inventory-microservice", "inventory-microservice" },
            }),
            NullLogger<AzureServiceBusDeadLetterCapture>.Instance);

        await capture.StartAsync(CancellationToken.None);

        client.Received(1).CreateProcessor("ecommerce-topic", "inventory-microservice", Arg.Any<ServiceBusProcessorOptions>());

        await capture.DisposeAsync();
    }

    [Fact]
    public async Task Given_one_processor_fails_to_start_When_StartAsync_Then_other_processors_remain_started()
    {
        var client = Substitute.For<ServiceBusClient>();
        var failing = Substitute.For<ServiceBusProcessor>();
        failing.StartProcessingAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("boom")));
        var healthy = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("ecommerce-topic", "inventory-microservice", Arg.Any<ServiceBusProcessorOptions>()).Returns(failing);
        client.CreateProcessor("ecommerce-topic", "payment-microservice", Arg.Any<ServiceBusProcessorOptions>()).Returns(healthy);

        var logger = new RecordingLogger();
        var capture = new AzureServiceBusDeadLetterCapture(
            new ServiceCollection().BuildServiceProvider(),
            client,
            Options.Create(new AzureServiceBusOptions
            {
                TopicName = "ecommerce-topic",
                DeadLetterCaptureSubscriptions = new List<string> { "inventory-microservice", "payment-microservice" },
            }),
            logger);

        await capture.StartAsync(CancellationToken.None);

        await healthy.Received(1).StartProcessingAsync(Arg.Any<CancellationToken>());

        Assert.Contains(logger.Messages, m =>
            m.Contains("inventory-microservice", StringComparison.Ordinal)
            && m.Contains("ecommerce-topic", StringComparison.Ordinal)
            && m.Contains(MessagingOptions.AzureServiceBusProvider, StringComparison.Ordinal));

        await capture.StopAsync(CancellationToken.None);
        await healthy.Received(1).StopProcessingAsync(Arg.Any<CancellationToken>());
        await failing.DidNotReceive().StopProcessingAsync(Arg.Any<CancellationToken>());

        await capture.DisposeAsync();
    }

    private static AzureServiceBusDeadLetterCapture BuildCapture(IDeadLetterStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        return new AzureServiceBusDeadLetterCapture(
            services.BuildServiceProvider(),
            Substitute.For<ServiceBusClient>(),
            Options.Create(new AzureServiceBusOptions
            {
                TopicName = "ecommerce-topic",
                DeadLetterCaptureSubscriptions = new List<string> { "inventory-microservice" },
            }),
            NullLogger<AzureServiceBusDeadLetterCapture>.Instance);
    }

    private sealed class RecordingLogger : ILogger<AzureServiceBusDeadLetterCapture>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private static ServiceBusReceivedMessage NewMessage(string subscription, string eventType) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(new { Payload = "x" })),
            subject: eventType,
            properties: new Dictionary<string, object>
            {
                [AzureServiceBusHostedService.OriginalQueueProperty] = subscription,
                [AzureServiceBusHostedService.EventTypeProperty] = eventType,
                [AzureServiceBusHostedService.ServiceProperty] = subscription,
                [AzureServiceBusHostedService.FailureReasonProperty] = "boom",
                [AzureServiceBusHostedService.AttemptsProperty] = 3,
                [AzureServiceBusHostedService.FailedAtProperty] = DateTime.UtcNow.ToString("O"),
            },
            deliveryCount: 3);

    private static List<KeyValuePair<string, object?>> StartListening(out ManualResetEventSlim observed)
    {
        var tags = new List<KeyValuePair<string, object?>>();
        var ev = new ManualResetEventSlim();
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AzureServiceBusDeadLetterCapture.MeterName
                && instrument.Name == "dlq_messages_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, measurementTags, _) =>
        {
            foreach (var tag in measurementTags)
            {
                tags.Add(new KeyValuePair<string, object?>(tag.Key, tag.Value));
            }
            ev.Set();
        });
        listener.Start();
        observed = ev;
        return tags;
    }

    private sealed class RecordingStore : IDeadLetterStore
    {
        public List<DeadLetterMessage> Captured { get; } = [];
        public bool ThrowOnCapture { get; set; }

        public Task CaptureAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
        {
            if (ThrowOnCapture)
            {
                throw new InvalidOperationException("store unavailable");
            }
            Captured.Add(message);
            return Task.CompletedTask;
        }

        public Task<DeadLetterPage> ListAsync(DeadLetterFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeadLetterMessage?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkReplayedAsync(Guid id, string replayedBy, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkDiscardedAsync(Guid id, string discardedBy, string discardReason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
