using System.Text;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.RabbitMq;
using ECommerce.Shared.IntegrationEvents.Commands;
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

    [Fact]
    public async Task Given_event_with_correlation_id_When_dead_lettered_Then_correlation_header_round_trips_to_dlq()
    {
        var queueName = $"shared-tests-corr-{Guid.NewGuid():N}";
        var correlationId = Guid.NewGuid();

        await using var host = BuildHost<TestEvent, RecordingHandler>(queueName, retryCount: 1,
            handler => handler.Mode = HandlerMode.AlwaysFail);

        await host.StartHostedServiceAsync();
        await WaitForQueueAsync(queueName);

        PublishWithCorrelationId(new TestEvent { Payload = "trace-me", CorrelationId = correlationId }, correlationId);

        Assert.True(await host.Handler.WaitForCallsAsync(2, TimeSpan.FromSeconds(10)));

        var deadLettered = await PollForMessageAsync(RabbitMqTopology.DeadLetterQueueName, TimeSpan.FromSeconds(5));
        Assert.NotNull(deadLettered);

        var headerValue = ReadHeaderString(deadLettered.BasicProperties, RabbitMqTopology.CorrelationIdHeader);
        Assert.Equal(correlationId.ToString(), headerValue);
        Assert.Equal(correlationId.ToString(), deadLettered.BasicProperties.CorrelationId);
    }

    [Fact]
    public async Task Given_dead_letter_replay_request_When_RabbitMq_publisher_publishes_Then_original_queue_receives_message_with_event_type_header()
    {
        var queueName = $"shared-tests-replay-{Guid.NewGuid():N}";
        const string payload = """{"Payload":"replayed"}""";
        var failureId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        using (var channel = _testConnection!.CreateModel())
        {
            channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);
        }

        var publisher = new RabbitMqDeadLetterPublisher(new ExistingConnection(_testConnection!));

        var newMessageId = publisher.Publish(new DeadLetterReplayRequest(
            OriginalQueue: queueName,
            EventType: nameof(TestEvent),
            Payload: payload,
            CorrelationId: correlationId,
            FailureId: failureId));

        var replayed = await PollForMessageAsync(queueName, TimeSpan.FromSeconds(5));

        Assert.NotNull(replayed);
        Assert.Equal(payload, Encoding.UTF8.GetString(replayed.Body.Span));
        Assert.Equal(newMessageId.ToString(), replayed.BasicProperties.MessageId);
        Assert.Equal(correlationId.ToString(), replayed.BasicProperties.CorrelationId);
        Assert.Equal(nameof(TestEvent), ReadHeaderString(replayed.BasicProperties, RabbitMqTopology.EventTypeHeader));
        Assert.Equal(failureId.ToString(), ReadHeaderString(replayed.BasicProperties, RabbitMqTopology.ReplayedFromHeader));
        Assert.Equal(correlationId.ToString(), ReadHeaderString(replayed.BasicProperties, RabbitMqTopology.CorrelationIdHeader));
    }

    [Fact]
    public async Task Given_saga_reserve_stock_command_dead_letters_When_gateway_capture_replays_Then_original_queue_resumes()
    {
        var queueName = $"inventory-saga-dlq-{Guid.NewGuid():N}";
        var command = new ReserveStockCommand(
            Guid.NewGuid(),
            "customer-saga",
            [new ReserveStockItem("MALFORMED", 1, 12.50m)],
            "USD",
            Guid.NewGuid(),
            Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid()
        };

        await using var captureProvider = BuildDeadLetterProvider();
        var capturer = captureProvider.GetRequiredService<RabbitMqDeadLetterCapture>();
        await capturer.StartAsync(CancellationToken.None);

        await using var host = BuildReserveStockHost(queueName, retryCount: 1, handler => handler.FailMalformedCommands = true);
        await host.StartHostedServiceAsync();
        await WaitForQueueAsync(queueName);
        await WaitForQueueAsync(RabbitMqTopology.DeadLetterQueueName);

        PublishWithCorrelationId(command, command.CorrelationId!.Value);

        Assert.True(await host.Handler.WaitForFailuresAsync(2, TimeSpan.FromSeconds(10)));

        var captured = await WaitForCapturedMessageAsync(
            captureProvider,
            command.OrderId.ToString(),
            TimeSpan.FromSeconds(15));
        Assert.True(
            captured is not null,
            $"Expected gateway DLQ capture row. DLQ count: {GetMessageCount(RabbitMqTopology.DeadLetterQueueName)}; captured rows: {await CountCapturedRowsAsync(captureProvider)}.");
        var message = captured!;
        Assert.Equal(nameof(ReserveStockCommand), message.EventType);
        Assert.Equal(queueName, message.OriginalQueue);
        Assert.Equal(queueName, message.Service);
        Assert.Equal(DeadLetterOrigin.DeadLetter, message.Origin);
        Assert.Equal(command.CorrelationId, message.CorrelationId);

        host.Handler.FailMalformedCommands = false;
        using (var replayScope = captureProvider.CreateScope())
        {
            var replayer = replayScope.ServiceProvider.GetRequiredService<IDeadLetterReplayer>();
            var result = await replayer.ReplayAsync(message.Id, "operator-test");
            Assert.Equal(DeadLetterReplayOutcome.Success, result.Outcome);
            Assert.NotNull(result.NewMessageId);
        }

        Assert.True(await host.Handler.WaitForSuccessesAsync(1, TimeSpan.FromSeconds(10)));
        Assert.Contains(host.Handler.SuccessfulCommands, c => c.OrderId == command.OrderId);

        var replayed = await WaitForCapturedStatusAsync(captureProvider, message.Id, DeadLetterStatus.Replayed, TimeSpan.FromSeconds(5));
        Assert.True(replayed, "DLQ row was not marked replayed after successful publish.");

        await capturer.StopAsync(CancellationToken.None);
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

    private ReserveStockTestHost BuildReserveStockHost(
        string queueName,
        int retryCount,
        Action<ReserveStockReplayHandler> configure)
    {
        var handler = new ReserveStockReplayHandler();
        configure(handler);

        var services = new ServiceCollection();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.Configure<EventBusOptions>(o => o.QueueName = queueName);
        services.Configure<RabbitMqOptions>(o => o.HandlerRetryCount = retryCount);
        services.Configure<EventHandlerRegistration>(o => o.EventTypes[typeof(ReserveStockCommand).Name] = typeof(ReserveStockCommand));

        services.AddSingleton<IRabbitMqConnection>(new ExistingConnection(_testConnection!));
        services.AddSingleton<RabbitMqTelemetry>();
        services.AddKeyedSingleton<IEventHandler>(typeof(ReserveStockCommand), handler);
        services.AddSingleton<RabbitMqHostedService>();

        var provider = services.BuildServiceProvider();
        return new ReserveStockTestHost(provider, provider.GetRequiredService<RabbitMqHostedService>(), handler);
    }

    private ServiceProvider BuildDeadLetterProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<GatewayDeadLetterTable>();
        services.AddSingleton<IDeadLetterStore>(sp => sp.GetRequiredService<GatewayDeadLetterTable>());
        services.AddSingleton<IRabbitMqConnection>(new ExistingConnection(_testConnection!));
        services.AddSingleton<IDeadLetterPublisher, RabbitMqDeadLetterPublisher>();
        services.AddScoped<IDeadLetterReplayer, DeadLetterReplayer>();
        services.AddSingleton<RabbitMqDeadLetterCapture>();
        return services.BuildServiceProvider();
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

    private void PublishWithCorrelationId(Event @event, Guid correlationId)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());
        using var channel = _testConnection!.CreateModel();
        var properties = channel.CreateBasicProperties();
        properties.CorrelationId = correlationId.ToString();
        properties.Headers = new Dictionary<string, object>
        {
            [RabbitMqTopology.CorrelationIdHeader] = Encoding.UTF8.GetBytes(correlationId.ToString()),
        };
        channel.BasicPublish(
            exchange: RabbitMqTopology.ExchangeName,
            routingKey: @event.GetType().Name,
            mandatory: false,
            basicProperties: properties,
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

    private static async Task<DeadLetterMessage?> WaitForCapturedMessageAsync(
        IServiceProvider provider,
        string payloadNeedle,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var store = provider.GetRequiredService<GatewayDeadLetterTable>();
            var page = await store.ListAsync(new DeadLetterFilter(PageSize: 200));
            var message = page.Items
                .OrderByDescending(m => m.FailedAt)
                .FirstOrDefault(m => m.Payload.Contains(payloadNeedle, StringComparison.Ordinal));
            if (message is not null)
            {
                return message;
            }

            await Task.Delay(100);
        }

        return null;
    }

    private static async Task<bool> WaitForCapturedStatusAsync(
        IServiceProvider provider,
        Guid id,
        DeadLetterStatus status,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var store = provider.GetRequiredService<GatewayDeadLetterTable>();
            var message = await store.GetAsync(id);
            if (message?.Status == status)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<int> CountCapturedRowsAsync(IServiceProvider provider)
    {
        var store = provider.GetRequiredService<GatewayDeadLetterTable>();
        var page = await store.ListAsync(new DeadLetterFilter(PageSize: 200));
        return page.TotalCount;
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

    // Mirrors RabbitMqDeadLetterCapture.ReadFirstDeathHeader: when the consumer cannot
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

    private sealed class ReserveStockTestHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly RabbitMqHostedService _service;

        public ReserveStockTestHost(
            ServiceProvider provider,
            RabbitMqHostedService service,
            ReserveStockReplayHandler handler)
        {
            _provider = provider;
            _service = service;
            Handler = handler;
        }

        public ReserveStockReplayHandler Handler { get; }

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

    public sealed class ReserveStockReplayHandler : IEventHandler<ReserveStockCommand>
    {
        private int _failures;
        private int _successes;
        private readonly object _gate = new();
        private TaskCompletionSource _failureSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _successSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _waitForFailures;
        private int _waitForSuccesses;

        public bool FailMalformedCommands { get; set; }

        public List<ReserveStockCommand> SuccessfulCommands { get; } = [];

        public Task Handle(ReserveStockCommand command)
        {
            if (FailMalformedCommands && command.Items.Any(i => i.ProductId == "MALFORMED"))
            {
                var failures = Interlocked.Increment(ref _failures);
                SignalIfReached(failures, isFailure: true);
                throw new InvalidOperationException("malformed ReserveStockCommand");
            }

            lock (_gate)
            {
                SuccessfulCommands.Add(command);
            }

            var successes = Interlocked.Increment(ref _successes);
            SignalIfReached(successes, isFailure: false);
            return Task.CompletedTask;
        }

        public Task<bool> WaitForFailuresAsync(int count, TimeSpan timeout) =>
            WaitForAsync(count, timeout, isFailure: true);

        public Task<bool> WaitForSuccessesAsync(int count, TimeSpan timeout) =>
            WaitForAsync(count, timeout, isFailure: false);

        private async Task<bool> WaitForAsync(int count, TimeSpan timeout, bool isFailure)
        {
            TaskCompletionSource tcs;
            lock (_gate)
            {
                var current = isFailure ? Volatile.Read(ref _failures) : Volatile.Read(ref _successes);
                if (current >= count)
                {
                    return true;
                }

                if (isFailure)
                {
                    _waitForFailures = count;
                    _failureSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs = _failureSignal;
                }
                else
                {
                    _waitForSuccesses = count;
                    _successSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs = _successSignal;
                }
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            return completed == tcs.Task;
        }

        private void SignalIfReached(int current, bool isFailure)
        {
            TaskCompletionSource? toSignal = null;
            lock (_gate)
            {
                if (isFailure && _waitForFailures > 0 && current >= _waitForFailures)
                {
                    toSignal = _failureSignal;
                }
                else if (!isFailure && _waitForSuccesses > 0 && current >= _waitForSuccesses)
                {
                    toSignal = _successSignal;
                }
            }

            toSignal?.TrySetResult();
        }
    }

    private sealed class GatewayDeadLetterTable : IDeadLetterStore
    {
        private readonly object _gate = new();
        private readonly List<DeadLetterMessage> _messages = [];

        public Task CaptureAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _messages.Add(message);
            }

            return Task.CompletedTask;
        }

        public Task<DeadLetterPage> ListAsync(DeadLetterFilter filter, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var query = _messages.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(filter.Service))
                {
                    query = query.Where(m => m.Service == filter.Service);
                }

                if (!string.IsNullOrWhiteSpace(filter.EventType))
                {
                    query = query.Where(m => m.EventType == filter.EventType);
                }

                if (filter.Status.HasValue)
                {
                    query = query.Where(m => m.Status == filter.Status.Value);
                }

                if (filter.From.HasValue)
                {
                    query = query.Where(m => m.FailedAt >= filter.From.Value);
                }

                if (filter.To.HasValue)
                {
                    query = query.Where(m => m.FailedAt <= filter.To.Value);
                }

                if (filter.Origin.HasValue)
                {
                    query = query.Where(m => m.Origin == filter.Origin.Value);
                }

                var page = Math.Max(1, filter.Page);
                var pageSize = Math.Clamp(filter.PageSize, 1, 200);
                var total = query.Count();
                var items = query
                    .OrderByDescending(m => m.FailedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToArray();

                return Task.FromResult(new DeadLetterPage(items, page, pageSize, total));
            }
        }

        public Task<DeadLetterMessage?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_messages.FirstOrDefault(m => m.Id == id));
            }
        }

        public Task<bool> MarkReplayedAsync(Guid id, string replayedBy, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var message = _messages.FirstOrDefault(m => m.Id == id);
                if (message is null || message.Status != DeadLetterStatus.Pending)
                {
                    return Task.FromResult(false);
                }

                message.Status = DeadLetterStatus.Replayed;
                message.ReplayedAt = DateTime.UtcNow;
                message.ReplayedBy = replayedBy;
                return Task.FromResult(true);
            }
        }

        public Task<bool> MarkDiscardedAsync(Guid id, string discardedBy, string discardReason, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var message = _messages.FirstOrDefault(m => m.Id == id);
                if (message is null || message.Status != DeadLetterStatus.Pending)
                {
                    return Task.FromResult(false);
                }

                message.Status = DeadLetterStatus.Discarded;
                message.DiscardedAt = DateTime.UtcNow;
                message.DiscardedBy = discardedBy;
                message.DiscardReason = discardReason;
                return Task.FromResult(true);
            }
        }
    }
}
