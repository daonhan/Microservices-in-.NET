using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ECommerce.Shared.Infrastructure.RabbitMq;

public partial class RabbitMqHostedService : IHostedService
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Handler for {EventType} on queue {Queue} failed after {Attempts} attempts; dead-lettering.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception ex, string eventType, string queue, int attempts);


    private readonly IServiceProvider _serviceProvider;
    private readonly EventHandlerRegistration _handlerRegistrations;
    private readonly EventBusOptions _eventBusOptions;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ActivitySource _activitySource;
    private readonly ILogger<RabbitMqHostedService> _logger;
    private readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;
    private readonly ResiliencePipeline _handlerPipeline;
    private IModel? _channel;

    public RabbitMqHostedService(IServiceProvider serviceProvider,
        IOptions<EventHandlerRegistration> handlerRegistrations,
        IOptions<EventBusOptions> eventBusOptions,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        RabbitMqTelemetry rabbitMqTelemetry,
        ILogger<RabbitMqHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _handlerRegistrations = handlerRegistrations.Value;
        _eventBusOptions = eventBusOptions.Value;
        _rabbitMqOptions = rabbitMqOptions.Value;
        _activitySource = rabbitMqTelemetry.ActivitySource;
        _logger = logger;
        _handlerPipeline = BuildHandlerPipeline(_rabbitMqOptions.HandlerRetryCount);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Factory.StartNew(() =>
        {
            var rabbitMQConnection = _serviceProvider.GetRequiredService<IRabbitMqConnection>();

            var channel = rabbitMQConnection.Connection.CreateModel();
            _channel = channel;

            DeclareTopology(channel, _eventBusOptions.QueueName);

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += OnMessageReceived;

            channel.BasicConsume(
                queue: _eventBusOptions.QueueName,
                autoAck: false,
                consumer: consumer,
                consumerTag: string.Empty,
                noLocal: false,
                exclusive: false,
                arguments: null);

            foreach (var (eventName, _) in _handlerRegistrations.EventTypes)
            {
                channel.QueueBind(
                    queue: _eventBusOptions.QueueName,
                    exchange: RabbitMqTopology.ExchangeName,
                    routingKey: eventName,
                    arguments: null);
            }
        },
        TaskCreationOptions.LongRunning);

        return Task.CompletedTask;
    }

    internal static void DeclareTopology(IModel channel, string queueName)
    {
        channel.ExchangeDeclare(
            exchange: RabbitMqTopology.ExchangeName,
            type: "fanout",
            durable: false,
            autoDelete: false,
            arguments: null);

        channel.ExchangeDeclare(
            exchange: RabbitMqTopology.DeadLetterExchangeName,
            type: "fanout",
            durable: true,
            autoDelete: false,
            arguments: null);

        channel.QueueDeclare(
            queue: RabbitMqTopology.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        channel.QueueBind(
            queue: RabbitMqTopology.DeadLetterQueueName,
            exchange: RabbitMqTopology.DeadLetterExchangeName,
            routingKey: string.Empty,
            arguments: null);

        var queueArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = RabbitMqTopology.DeadLetterExchangeName
        };

        channel.QueueDeclare(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs);
    }

    private void OnMessageReceived(object? sender, BasicDeliverEventArgs eventArgs)
    {
        var channel = ((EventingBasicConsumer)sender!).Model;

        var parentContext = _propagator.Extract(default, eventArgs.BasicProperties, (properties, key) =>
        {
            if (properties.Headers is not null && properties.Headers.TryGetValue(key, out var value))
            {
                var bytes = value as byte[];
                return [Encoding.UTF8.GetString(bytes!)];
            }

            return [];
        });

        var activityName = $"{OpenTelemetryMessagingConventions.ReceiveOperation} {eventArgs.RoutingKey}";
        using var activity = _activitySource.StartActivity(activityName, ActivityKind.Client,
            parentContext.ActivityContext);

        SetActivityContext(activity, eventArgs.RoutingKey, OpenTelemetryMessagingConventions.ReceiveOperation);

        var inboundCorrelationId = ReadCorrelationId(eventArgs.BasicProperties);
        if (inboundCorrelationId is not null)
        {
            activity?.SetTag("messaging.correlation_id", inboundCorrelationId);
        }

        // When a message is replayed via the default exchange (routingKey = queue name),
        // the original event type is carried in the x-event-type header. Prefer it when present
        // so the existing handler dispatch by event-type-name keeps working.
        var eventName = ReadEventTypeHeader(eventArgs.BasicProperties) ?? eventArgs.RoutingKey;
        var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

        activity?.SetTag("message", message);

        if (!_handlerRegistrations.EventTypes.TryGetValue(eventName, out var eventType))
        {
            channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
            return;
        }

        var attempts = 0;
        try
        {
            _handlerPipeline.Execute(_ =>
            {
                attempts++;
                using var scope = _serviceProvider.CreateScope();
                var @event = JsonSerializer.Deserialize(message, eventType) as Event;

                foreach (var handler in scope.ServiceProvider.GetKeyedServices<IEventHandler>(eventType))
                {
                    handler.Handle(@event!);
                }
            }, CancellationToken.None);

            channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            LogHandlerFailed(_logger, ex, eventName, _eventBusOptions.QueueName, attempts);

            EnrichHeadersForDeadLetter(eventArgs.BasicProperties, eventName, attempts, ex);
            channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private void EnrichHeadersForDeadLetter(IBasicProperties properties, string eventName, int attempts, Exception ex)
    {
        properties.Headers ??= new Dictionary<string, object>();
        properties.Headers[RabbitMqTopology.OriginalQueueHeader] = Encoding.UTF8.GetBytes(_eventBusOptions.QueueName);
        properties.Headers[RabbitMqTopology.EventTypeHeader] = Encoding.UTF8.GetBytes(eventName);
        properties.Headers[RabbitMqTopology.ServiceHeader] = Encoding.UTF8.GetBytes(_eventBusOptions.QueueName);
        properties.Headers[RabbitMqTopology.FailureReasonHeader] = Encoding.UTF8.GetBytes(Truncate($"{ex.GetType().FullName}: {ex.Message}", 1024));
        properties.Headers[RabbitMqTopology.StackTraceHeader] = Encoding.UTF8.GetBytes(Truncate(ex.ToString(), 16 * 1024));
        properties.Headers[RabbitMqTopology.AttemptsHeader] = attempts;
        properties.Headers[RabbitMqTopology.FailedAtHeader] = Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? ReadEventTypeHeader(IBasicProperties? properties)
    {
        if (properties?.Headers is null ||
            !properties.Headers.TryGetValue(RabbitMqTopology.EventTypeHeader, out var value))
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => value.ToString()
        };
    }

    private static string? ReadCorrelationId(IBasicProperties? properties)
    {
        if (properties is null)
        {
            return null;
        }

        if (properties.Headers is not null &&
            properties.Headers.TryGetValue(RabbitMqTopology.CorrelationIdHeader, out var headerValue))
        {
            var fromHeader = headerValue switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string s => s,
                _ => headerValue?.ToString()
            };

            if (!string.IsNullOrWhiteSpace(fromHeader))
            {
                return fromHeader;
            }
        }

        return string.IsNullOrWhiteSpace(properties.CorrelationId) ? null : properties.CorrelationId;
    }

    private static ResiliencePipeline BuildHandlerPipeline(int retryCount)
    {
        var retry = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<Exception>(),
            MaxRetryAttempts = Math.Max(0, retryCount),
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(200)
        };

        return new ResiliencePipelineBuilder().AddRetry(retry).Build();
    }

    private static void SetActivityContext(Activity? activity, string routingKey, string operation)
    {
        if (activity is not null)
        {
            activity.SetTag(OpenTelemetryMessagingConventions.System, "rabbitmq");
            activity.SetTag(OpenTelemetryMessagingConventions.OperationName, operation);
            activity.SetTag(OpenTelemetryMessagingConventions.DestinationName, routingKey);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _channel?.Dispose();
        return Task.CompletedTask;
    }
}
