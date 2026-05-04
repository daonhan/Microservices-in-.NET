using System.Diagnostics.Metrics;
using System.Text;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
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
    public async Task Given_dead_lettered_message_with_service_header_When_capturer_consumes_Then_dlq_messages_total_is_tagged_with_service_and_event_type()
    {
        // AC #7 of issue #41: per-service dlq_messages_total rows with the correct `service` label.
        // RabbitMqHostedService stamps `x-service` from EventBusOptions.QueueName before BasicNack
        // (RabbitMqHostedService.cs:199). DeadLetterHostedService reads that header into
        // DeadLetterMessage.Service and tags the counter (DeadLetterHostedService.cs:91-93).
        // This test exercises the capture half end-to-end: publish a real DLQ message with the
        // headers RabbitMqHostedService would emit, then assert the metric tags match.
        const string serviceName = "basket-microservice";
        const string eventType = nameof(MetricTestEvent);

        var capturedTags = new List<KeyValuePair<string, object?>>();
        var metricEmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DeadLetterHostedService.MeterName
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
                    if (tag.Key == "service" && (string?)tag.Value == serviceName)
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
        services.AddSingleton<DeadLetterHostedService>();
        await using var provider = services.BuildServiceProvider();

        var capturer = provider.GetRequiredService<DeadLetterHostedService>();
        await capturer.StartAsync(CancellationToken.None);

        await WaitForQueueAsync(RabbitMqTopology.DeadLetterQueueName);

        PublishDeadLetter(serviceName, eventType, payload: """{"Payload":"doomed"}""");

        var captured = await fakeStore.WaitForCaptureAsync(TimeSpan.FromSeconds(15));
        var metricSeen = await Task.WhenAny(metricEmitted.Task, Task.Delay(TimeSpan.FromSeconds(15)))
            == metricEmitted.Task;

        await capturer.StopAsync(CancellationToken.None);

        Assert.True(captured, "DLQ capture did not occur within timeout.");
        Assert.True(metricSeen, "dlq_messages_total was not observed for the expected service.");

        KeyValuePair<string, object?>[] snapshot;
        lock (capturedTags)
        {
            snapshot = capturedTags.ToArray();
        }

        Assert.Contains(snapshot, t => t.Key == "service" && (string?)t.Value == serviceName);
        Assert.Contains(snapshot, t => t.Key == "event_type" && (string?)t.Value == eventType);
        Assert.Equal(serviceName, fakeStore.LastMessage?.Service);
        Assert.Equal(eventType, fakeStore.LastMessage?.EventType);
    }

    private void PublishDeadLetter(string serviceName, string eventType, string payload)
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
            [RabbitMqTopology.OriginalQueueHeader] = Encoding.UTF8.GetBytes(serviceName),
            [RabbitMqTopology.EventTypeHeader] = Encoding.UTF8.GetBytes(eventType),
            [RabbitMqTopology.ServiceHeader] = Encoding.UTF8.GetBytes(serviceName),
            [RabbitMqTopology.FailureReasonHeader] = Encoding.UTF8.GetBytes("test failure"),
            [RabbitMqTopology.AttemptsHeader] = 2,
        };

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

        public DeadLetterMessage? LastMessage { get; private set; }

        public Task CaptureAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
        {
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

        public async Task<bool> WaitForCaptureAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_captured.Task, Task.Delay(timeout));
            return completed == _captured.Task;
        }
    }
}
