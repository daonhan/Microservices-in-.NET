using System.Diagnostics.Metrics;
using System.Text;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace ECommerce.Shared.Tests;

[Trait("Category", "Integration")]
public sealed class DlqMetricAttributionTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
        .Build();

    private IConnection? _connection;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = new ConnectionFactory { Uri = new Uri(_container.GetConnectionString()) }.CreateConnection();
    }

    public async Task DisposeAsync()
    {
        _connection?.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Given_dead_lettered_message_with_service_header_When_capturer_consumes_Then_dlq_messages_total_is_tagged_with_provider_service_and_event_type()
    {
        // AC #7 of issue #41: per-service dlq_messages_total rows with the correct `service` label.
        // RabbitMqHostedService stamps `x-service` from EventBusOptions.QueueName before BasicNack
        // (RabbitMqHostedService.cs:199). RabbitMqDeadLetterCapture reads that header into
        // DeadLetterMessage.Service and tags the counter.
        // This test exercises the capture half end-to-end: publish a real DLQ message with the
        // headers RabbitMqHostedService would emit, then assert the metric tags match.
        const string serviceName = "basket-microservice";
        const string eventType = nameof(MetricTestEvent);

        var capturedTags = new List<KeyValuePair<string, object?>>();
        var metricEmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == RabbitMqDeadLetterCapture.MeterName
                && instrument.Name == "dlq_messages_total")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var matchedThisCallback = false;
            lock (capturedTags)
            {
                foreach (var tag in tags)
                {
                    capturedTags.Add(new KeyValuePair<string, object?>(tag.Key, tag.Value));
                    if (tag.Key == "provider" && (string?)tag.Value == MessagingOptions.RabbitMqProvider)
                    {
                        matchedThisCallback = true;
                    }
                }
            }

            if (matchedThisCallback)
            {
                metricEmitted.TrySetResult();
            }
        });
        meterListener.Start();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IRabbitMqConnection>(new ExistingConnection(_connection!));
        var fakeStore = new RecordingDeadLetterStore();
        services.AddSingleton<IDeadLetterStore>(fakeStore);
        services.AddSingleton<RabbitMqDeadLetterCapture>();
        await using var provider = services.BuildServiceProvider();

        var capturer = provider.GetRequiredService<RabbitMqDeadLetterCapture>();
        await capturer.StartAsync(CancellationToken.None);

        await WaitForQueueAsync(RabbitMqTopology.DeadLetterQueueName);

        PublishDeadLetter(serviceName, eventType, payload: """{"Payload":"doomed"}""");

        var captured = await fakeStore.WaitForCaptureAsync(TimeSpan.FromSeconds(15));
        var metricSeen = await Task.WhenAny(metricEmitted.Task, Task.Delay(TimeSpan.FromSeconds(15)))
            == metricEmitted.Task;

        await capturer.StopAsync(CancellationToken.None);

        Assert.True(captured, "DLQ capture did not occur within timeout.");
        Assert.True(metricSeen, "dlq_messages_total was not tagged with provider=RabbitMq.");

        KeyValuePair<string, object?>[] snapshot;
        lock (capturedTags)
        {
            snapshot = capturedTags.ToArray();
        }

        Assert.Contains(snapshot, t => t.Key == "provider" && (string?)t.Value == MessagingOptions.RabbitMqProvider);
        Assert.Contains(snapshot, t => t.Key == "service" && (string?)t.Value == serviceName);
        Assert.Contains(snapshot, t => t.Key == "event_type" && (string?)t.Value == eventType);
        Assert.Equal(serviceName, fakeStore.LastMessage?.Service);
        Assert.Equal(eventType, fakeStore.LastMessage?.EventType);
    }

    [Fact]
    public async Task Given_RabbitMq_dead_lettered_message_When_capturer_consumes_Then_store_receives_normalized_message_and_queue_is_acked()
    {
        const string serviceName = "order-microservice";
        const string eventType = nameof(MetricTestEvent);
        const string originalQueue = "order-subscription";
        const string payload = """{"Payload":"captured"}""";
        const string failureReason = "handler exhausted retry budget";
        const string stackTrace = "System.InvalidOperationException: boom";
        const int attempts = 4;
        var failedAt = new DateTime(2026, 5, 14, 1, 2, 3, DateTimeKind.Utc);
        var correlationId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IRabbitMqConnection>(new ExistingConnection(_connection!));
        var fakeStore = new RecordingDeadLetterStore();
        services.AddSingleton<IDeadLetterStore>(fakeStore);
        services.AddSingleton<RabbitMqDeadLetterCapture>();
        await using var provider = services.BuildServiceProvider();

        var capturer = provider.GetRequiredService<RabbitMqDeadLetterCapture>();
        await capturer.StartAsync(CancellationToken.None);

        await WaitForQueueAsync(RabbitMqTopology.DeadLetterQueueName);

        PublishDeadLetter(
            serviceName,
            eventType,
            payload,
            originalQueue,
            failureReason,
            stackTrace,
            attempts,
            failedAt,
            correlationId);

        var captured = await fakeStore.WaitForCaptureAsync(TimeSpan.FromSeconds(15));
        var acked = await WaitForMessageCountAsync(RabbitMqTopology.DeadLetterQueueName, expectedCount: 0, TimeSpan.FromSeconds(5));

        await capturer.StopAsync(CancellationToken.None);

        Assert.True(captured, "DLQ capture did not occur within timeout.");
        Assert.True(acked, "DLQ message was not acked after capture.");

        var message = Assert.IsType<DeadLetterMessage>(fakeStore.LastMessage);
        Assert.Equal(eventType, message.EventType);
        Assert.Equal(eventType, message.RoutingKey);
        Assert.Equal(originalQueue, message.OriginalQueue);
        Assert.Equal(serviceName, message.Service);
        Assert.Equal(payload, message.Payload);
        Assert.Equal(failureReason, message.FailureReason);
        Assert.Equal(stackTrace, message.StackTrace);
        Assert.Equal(attempts, message.Attempts);
        Assert.Equal(failedAt, message.FailedAt);
        Assert.Equal(correlationId, message.CorrelationId);
        Assert.Equal(DeadLetterOrigin.DeadLetter, message.Origin);
    }

    [Fact]
    public async Task Given_store_capture_throws_When_capturer_consumes_Then_message_is_nacked_requeued_and_captured_again()
    {
        const string serviceName = "payment-microservice";
        const string eventType = nameof(MetricTestEvent);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IRabbitMqConnection>(new ExistingConnection(_connection!));
        var fakeStore = new RecordingDeadLetterStore(failuresBeforeSuccess: 1);
        services.AddSingleton<IDeadLetterStore>(fakeStore);
        services.AddSingleton<RabbitMqDeadLetterCapture>();
        await using var provider = services.BuildServiceProvider();

        var capturer = provider.GetRequiredService<RabbitMqDeadLetterCapture>();
        await capturer.StartAsync(CancellationToken.None);

        await WaitForQueueAsync(RabbitMqTopology.DeadLetterQueueName);

        PublishDeadLetter(serviceName, eventType, payload: """{"Payload":"retry-capture"}""");

        var capturedAfterRetry = await fakeStore.WaitForCaptureAsync(TimeSpan.FromSeconds(15));
        var acked = await WaitForMessageCountAsync(RabbitMqTopology.DeadLetterQueueName, expectedCount: 0, TimeSpan.FromSeconds(5));

        await capturer.StopAsync(CancellationToken.None);

        Assert.True(capturedAfterRetry, "DLQ capture did not succeed after the store failure.");
        Assert.True(acked, "DLQ message was not acked after the retry capture succeeded.");
        Assert.True(fakeStore.CaptureAttempts >= 2, $"Expected at least two capture attempts, saw {fakeStore.CaptureAttempts}.");
        Assert.Equal(serviceName, fakeStore.LastMessage?.Service);
    }

    private void PublishDeadLetter(
        string serviceName,
        string eventType,
        string payload,
        string? originalQueue = null,
        string failureReason = "test failure",
        string? stackTrace = null,
        int attempts = 2,
        DateTime? failedAt = null,
        Guid? correlationId = null)
    {
        using var channel = _connection!.CreateModel();

        channel.ExchangeDeclare(
            exchange: RabbitMqTopology.DeadLetterExchangeName,
            type: "fanout",
            durable: true,
            autoDelete: false,
            arguments: null);

        var props = channel.CreateBasicProperties();
        props.Headers = new Dictionary<string, object>
        {
            [RabbitMqTopology.OriginalQueueHeader] = Encoding.UTF8.GetBytes(originalQueue ?? serviceName),
            [RabbitMqTopology.EventTypeHeader] = Encoding.UTF8.GetBytes(eventType),
            [RabbitMqTopology.ServiceHeader] = Encoding.UTF8.GetBytes(serviceName),
            [RabbitMqTopology.FailureReasonHeader] = Encoding.UTF8.GetBytes(failureReason),
            [RabbitMqTopology.AttemptsHeader] = attempts,
        };
        if (stackTrace is not null)
        {
            props.Headers[RabbitMqTopology.StackTraceHeader] = Encoding.UTF8.GetBytes(stackTrace);
        }

        if (failedAt is not null)
        {
            props.Headers[RabbitMqTopology.FailedAtHeader] = Encoding.UTF8.GetBytes(failedAt.Value.ToString("O"));
        }

        if (correlationId is not null)
        {
            props.CorrelationId = correlationId.Value.ToString();
            props.Headers[RabbitMqTopology.CorrelationIdHeader] = Encoding.UTF8.GetBytes(correlationId.Value.ToString());
        }

        channel.BasicPublish(
            exchange: RabbitMqTopology.DeadLetterExchangeName,
            routingKey: eventType,
            mandatory: false,
            basicProperties: props,
            body: Encoding.UTF8.GetBytes(payload));
    }

    private async Task WaitForQueueAsync(string queueName, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var channel = _connection!.CreateModel();
                channel.QueueDeclarePassive(queueName);
                await Task.Delay(150);
                return;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                await Task.Delay(50);
            }
        }

        throw new TimeoutException($"Queue {queueName} was not declared in time.");
    }

    private async Task<bool> WaitForMessageCountAsync(string queueName, uint expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var channel = _connection!.CreateModel();
            var declareOk = channel.QueueDeclarePassive(queueName);
            if (declareOk.MessageCount == expectedCount)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    public sealed record MetricTestEvent
    {
        public string Payload { get; init; } = string.Empty;
    }

    private sealed class ExistingConnection : IRabbitMqConnection
    {
        public ExistingConnection(IConnection connection) => Connection = connection;

        public IConnection Connection { get; }
    }

    private sealed class RecordingDeadLetterStore : IDeadLetterStore
    {
        private readonly TaskCompletionSource _captured = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remainingFailures;

        public RecordingDeadLetterStore(int failuresBeforeSuccess = 0)
        {
            _remainingFailures = failuresBeforeSuccess;
        }

        public DeadLetterMessage? LastMessage { get; private set; }

        public int CaptureAttempts { get; private set; }

        public Task CaptureAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
        {
            CaptureAttempts++;
            if (_remainingFailures > 0)
            {
                _remainingFailures--;
                throw new InvalidOperationException("store unavailable");
            }

            LastMessage = message;
            _captured.TrySetResult();
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

        public async Task<bool> WaitForCaptureAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_captured.Task, Task.Delay(timeout));
            return completed == _captured.Task;
        }
    }
}
