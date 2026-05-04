using System.Text;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace ECommerce.Shared.Tests;

[Trait("Category", "Integration")]
public sealed class RabbitMqDeadLetterIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
        .Build();

    private IConnection? _testConnection;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var factory = new ConnectionFactory { Uri = new Uri(_container.GetConnectionString()) };
        _testConnection = factory.CreateConnection();
    }

    public async Task DisposeAsync()
    {
        _testConnection?.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Given_handler_succeeds_When_message_published_Then_message_is_acked_and_dlq_is_empty()
    {
        var queueName = $"shared-tests-success-{Guid.NewGuid():N}";
        await using var host = BuildHost<TestEvent, RecordingHandler>(queueName, retryCount: 1,
            handler => handler.Mode = HandlerMode.AlwaysSucceed);

        await host.StartHostedServiceAsync();
        await WaitForQueueAsync(queueName);

        Publish(new TestEvent { Payload = "ok" });

        Assert.True(await host.Handler.WaitForCallsAsync(1));
        await Task.Delay(300); // allow ack to flush

        Assert.Equal(0u, GetMessageCount(queueName));
        Assert.Equal(0u, GetMessageCount(RabbitMqTopology.DeadLetterQueueName));
    }

    [Fact]
    public async Task Given_handler_throws_once_with_retry_budget_one_When_message_published_Then_acked_on_second_attempt()
    {
        var queueName = $"shared-tests-retry-{Guid.NewGuid():N}";
        await using var host = BuildHost<TestEvent, RecordingHandler>(queueName, retryCount: 1,
            handler =>
            {
                handler.Mode = HandlerMode.FailFirstNCalls;
                handler.FailFirstN = 1;
            });

        await host.StartHostedServiceAsync();
        await WaitForQueueAsync(queueName);

        Publish(new TestEvent { Payload = "retry-me" });

        Assert.True(await host.Handler.WaitForCallsAsync(2, TimeSpan.FromSeconds(5)));
        await Task.Delay(300);

        Assert.Equal(0u, GetMessageCount(queueName));
        Assert.Equal(0u, GetMessageCount(RabbitMqTopology.DeadLetterQueueName));
    }

    [Fact]
    public async Task Given_handler_always_throws_When_message_published_Then_lands_on_dlq_with_original_queue_header()
    {
        var queueName = $"shared-tests-dlq-{Guid.NewGuid():N}";
        await using var host = BuildHost<TestEvent, RecordingHandler>(queueName, retryCount: 1,
            handler => handler.Mode = HandlerMode.AlwaysFail);

        await host.StartHostedServiceAsync();
        await WaitForQueueAsync(queueName);

        Publish(new TestEvent { Payload = "doomed" });

        Assert.True(await host.Handler.WaitForCallsAsync(2, TimeSpan.FromSeconds(10)));

        var deadLettered = await PollForMessageAsync(RabbitMqTopology.DeadLetterQueueName, TimeSpan.FromSeconds(5));
        Assert.NotNull(deadLettered);

        Assert.Equal(0u, GetMessageCount(queueName));
        var originalQueue = ReadHeaderString(deadLettered.BasicProperties, RabbitMqTopology.OriginalQueueHeader)
            ?? ReadFirstDeathQueue(deadLettered.BasicProperties);
        Assert.Equal(queueName, originalQueue);
    }

    [Fact]
    public async Task Given_consumer_restarted_When_topology_redeclared_Then_no_exception_is_thrown()
    {
        var queueName = $"shared-tests-restart-{Guid.NewGuid():N}";

        await using (var first = BuildHost<TestEvent, RecordingHandler>(queueName, retryCount: 1, _ => { }))
        {
            await first.StartHostedServiceAsync();
            await WaitForQueueAsync(queueName);
        }

        await using var second = BuildHost<TestEvent, RecordingHandler>(queueName, retryCount: 1, _ => { });
        await second.StartHostedServiceAsync();
        await WaitForQueueAsync(queueName);

        // If topology declarations were not idempotent, the second start would have thrown.
        Assert.Equal(0u, GetMessageCount(queueName));
    }

    private TestHost BuildHost<TEvent, THandler>(string queueName, int retryCount, Action<THandler> configure)
        where TEvent : Event
        where THandler : class, IEventHandler<TEvent>, new()
    {
        var handler = new THandler();
        configure(handler);

        var services = new ServiceCollection();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.Configure<EventBusOptions>(o => o.QueueName = queueName);
        services.Configure<RabbitMqOptions>(o => o.HandlerRetryCount = retryCount);
        services.Configure<EventHandlerRegistration>(o => o.EventTypes[typeof(TEvent).Name] = typeof(TEvent));

        services.AddSingleton<IRabbitMqConnection>(new ExistingConnection(_testConnection!));
        services.AddSingleton<RabbitMqTelemetry>();
        services.AddKeyedSingleton<IEventHandler>(typeof(TEvent), handler);
        services.AddSingleton<RabbitMqHostedService>();

        var provider = services.BuildServiceProvider();
        return new TestHost(provider, provider.GetRequiredService<RabbitMqHostedService>(), handler);
    }

    private void Publish(Event @event)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());
        using var channel = _testConnection!.CreateModel();
        channel.BasicPublish(
            exchange: RabbitMqTopology.ExchangeName,
            routingKey: @event.GetType().Name,
            mandatory: false,
            basicProperties: null,
            body: json);
    }

    private uint GetMessageCount(string queueName)
    {
        using var channel = _testConnection!.CreateModel();
        var declareOk = channel.QueueDeclarePassive(queueName);
        return declareOk.MessageCount;
    }

    private async Task WaitForQueueAsync(string queueName, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var channel = _testConnection!.CreateModel();
                channel.QueueDeclarePassive(queueName);
                // Give a brief moment for the consumer's QueueBind calls to register.
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

    private async Task<RabbitMQ.Client.BasicGetResult?> PollForMessageAsync(string queueName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var channel = _testConnection!.CreateModel();
            var result = channel.BasicGet(queueName, autoAck: true);
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(100);
        }

        return null;
    }

    private static string? ReadHeaderString(IBasicProperties props, string key)
    {
        if (props.Headers is null || !props.Headers.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => value.ToString(),
        };
    }

    // Mirrors DeadLetterHostedService.ReadFirstDeathHeader: when the consumer cannot
    // mutate properties before BasicNack, RabbitMQ's built-in x-death header still
    // identifies the original queue.
    private static string? ReadFirstDeathQueue(IBasicProperties props)
    {
        if (props.Headers is null || !props.Headers.TryGetValue("x-death", out var deathObj))
        {
            return null;
        }

        if (deathObj is not IList<object> entries || entries.Count == 0)
        {
            return null;
        }

        if (entries[0] is not IDictionary<string, object> entry)
        {
            return null;
        }

        if (!entry.TryGetValue("queue", out var queueValue))
        {
            return null;
        }

        return queueValue switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => queueValue.ToString(),
        };
    }

    private sealed class ExistingConnection : IRabbitMqConnection
    {
        public ExistingConnection(IConnection connection) => Connection = connection;

        public IConnection Connection { get; }
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly RabbitMqHostedService _service;

        public TestHost(ServiceProvider provider, RabbitMqHostedService service, object handler)
        {
            _provider = provider;
            _service = service;
            Handler = (RecordingHandler)handler;
        }

        public RecordingHandler Handler { get; }

        public Task StartHostedServiceAsync() => _service.StartAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await _service.StopAsync(CancellationToken.None);
            await _provider.DisposeAsync();
        }
    }

    public enum HandlerMode
    {
        AlwaysSucceed,
        FailFirstNCalls,
        AlwaysFail,
    }

    public sealed record TestEvent : Event
    {
        public string Payload { get; init; } = string.Empty;
    }

    public sealed class RecordingHandler : IEventHandler<TestEvent>
    {
        private int _calls;
        private readonly object _gate = new();
        private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _waitFor;

        public HandlerMode Mode { get; set; } = HandlerMode.AlwaysSucceed;
        public int FailFirstN { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public Task Handle(TestEvent @event)
        {
            var current = Interlocked.Increment(ref _calls);

            TaskCompletionSource? toSignal = null;
            lock (_gate)
            {
                if (_waitFor > 0 && current >= _waitFor)
                {
                    toSignal = _signal;
                }
            }
            toSignal?.TrySetResult();

            if (Mode == HandlerMode.AlwaysFail)
            {
                throw new InvalidOperationException($"always-fail call #{current}");
            }

            if (Mode == HandlerMode.FailFirstNCalls && current <= FailFirstN)
            {
                throw new InvalidOperationException($"fail-first-{FailFirstN} call #{current}");
            }

            return Task.CompletedTask;
        }

        public async Task<bool> WaitForCallsAsync(int count, TimeSpan? timeout = null)
        {
            TaskCompletionSource tcs;
            lock (_gate)
            {
                if (Calls >= count)
                {
                    return true;
                }

                _waitFor = count;
                _signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                tcs = _signal;
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout ?? TimeSpan.FromSeconds(5)));
            return completed == tcs.Task;
        }
    }
}
