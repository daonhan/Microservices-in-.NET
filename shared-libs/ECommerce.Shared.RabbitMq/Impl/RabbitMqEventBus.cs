using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Observability;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Polly;
using Polly.Retry;
using RabbitMQ.Client.Exceptions;

namespace ECommerce.Shared.Infrastructure.RabbitMq;

public class RabbitMqEventBus : IEventBus
{
    private const string ExchangeName = RabbitMqTopology.ExchangeName;

    private readonly IRabbitMqConnection _rabbitMqConnection;
    private readonly ActivitySource _activitySource;
    private readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;
    private readonly ResiliencePipeline _pipeline;

    public RabbitMqEventBus(IRabbitMqConnection rabbitMqConnection,
        RabbitMqTelemetry rabbitMqTelemetry,
        IOptions<EventBusOptions> options)
    {
        _rabbitMqConnection = rabbitMqConnection;
        _activitySource = rabbitMqTelemetry.ActivitySource;
        _pipeline = CreateResiliencePipeline(options.Value.RetryCount);
    }

    public Task PublishAsync(Event @event)
    {
        var routingKey = @event.GetType().Name;

        return _pipeline.Execute(() =>
        {
            using var channel = _rabbitMqConnection.Connection.CreateModel();
            channel.ConfirmSelect();
            var returned = false;
            channel.BasicReturn += (_, _) => Volatile.Write(ref returned, true);

            var activityName = $"{OpenTelemetryMessagingConventions.PublishOperation} {routingKey}";
            using var activity = _activitySource.StartActivity(activityName, ActivityKind.Client);

            ActivityContext activityContextToInject = default;
            if (activity is not null)
            {
                activityContextToInject = activity.Context;
            }

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = @event.Id.ToString();
            properties.ContentType = "application/json";
            properties.Type = routingKey;
            _propagator.Inject(new PropagationContext(activityContextToInject, Baggage.Current), properties,
                (properties, key, value) =>
                {
                    properties.Headers ??= new Dictionary<string, object>();
                    properties.Headers[key] = value;
                });

            if (@event.CorrelationId is { } correlationId)
            {
                properties.CorrelationId = correlationId.ToString();
                properties.Headers ??= new Dictionary<string, object>();
                properties.Headers[RabbitMqTopology.CorrelationIdHeader] =
                    System.Text.Encoding.UTF8.GetBytes(correlationId.ToString());
                activity?.SetTag("messaging.correlation_id", correlationId.ToString());
            }

            SetActivityContext(activity, routingKey, OpenTelemetryMessagingConventions.PublishOperation);

            channel.ExchangeDeclare(
                exchange: ExchangeName,
                type: "fanout",
                durable: false,
                autoDelete: false,
                null);

            var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());

            channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body);

            // Confirms cover broker acceptance, while BasicReturn detects an
            // unroutable mandatory publish (which can still receive an ack).
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(10));
            if (Volatile.Read(ref returned))
            {
                throw new InvalidOperationException($"RabbitMQ returned unroutable event {@event.Id} ({routingKey}).");
            }

            return Task.CompletedTask;
        });
    }

    private static ResiliencePipeline CreateResiliencePipeline(int retryCount)
    {
        var retryOptions = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<BrokerUnreachableException>()
                .Handle<SocketException>()
                .Handle<AlreadyClosedException>(),
            BackoffType = DelayBackoffType.Exponential,
            MaxRetryAttempts = retryCount
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retryOptions)
            .Build();
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
}
