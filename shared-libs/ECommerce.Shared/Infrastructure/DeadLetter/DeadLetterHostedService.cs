using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public sealed partial class DeadLetterHostedService : IHostedService
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Failed to capture dead-letter message; requeueing.")]
    private static partial void LogCaptureFailed(ILogger logger, Exception ex);


    public const string MeterName = "ECommerce.Shared.DeadLetter";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> CapturedCounter = Meter.CreateCounter<long>(
        "dlq_messages_total",
        description: "Number of messages captured into the dead-letter store");

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeadLetterHostedService> _logger;
    private IModel? _channel;

    public DeadLetterHostedService(IServiceProvider serviceProvider, ILogger<DeadLetterHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Factory.StartNew(() =>
        {
            var connection = _serviceProvider.GetRequiredService<IRabbitMqConnection>();
            var channel = connection.Connection.CreateModel();
            _channel = channel;

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

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += OnMessageReceived;

            channel.BasicConsume(
                queue: RabbitMqTopology.DeadLetterQueueName,
                autoAck: false,
                consumer: consumer);
        }, TaskCreationOptions.LongRunning);

        return Task.CompletedTask;
    }

    private void OnMessageReceived(object? sender, BasicDeliverEventArgs eventArgs)
    {
        var channel = ((EventingBasicConsumer)sender!).Model;
        var routingKey = eventArgs.RoutingKey;
        var payload = Encoding.UTF8.GetString(eventArgs.Body.Span);

        try
        {
            var message = BuildMessage(eventArgs, payload, routingKey);

            using var scope = _serviceProvider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
            store.CaptureAsync(message).GetAwaiter().GetResult();

            CapturedCounter.Add(1,
                new KeyValuePair<string, object?>("service", message.Service),
                new KeyValuePair<string, object?>("event_type", message.EventType));

            channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            LogCaptureFailed(_logger, ex);
            channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private static DeadLetterMessage BuildMessage(BasicDeliverEventArgs eventArgs, string payload, string routingKey)
    {
        var headers = eventArgs.BasicProperties?.Headers;

        var originalQueue = ReadHeaderString(headers, RabbitMqTopology.OriginalQueueHeader)
            ?? ReadFirstDeathHeader(headers, "queue")
            ?? string.Empty;
        var eventType = ReadHeaderString(headers, RabbitMqTopology.EventTypeHeader) ?? routingKey;
        var service = ReadHeaderString(headers, RabbitMqTopology.ServiceHeader) ?? originalQueue;
        var failureReason = ReadHeaderString(headers, RabbitMqTopology.FailureReasonHeader) ?? "unknown";
        var stackTrace = ReadHeaderString(headers, RabbitMqTopology.StackTraceHeader);
        var attempts = ReadHeaderInt(headers, RabbitMqTopology.AttemptsHeader) ?? ReadFirstDeathCount(headers) ?? 1;
        var failedAtRaw = ReadHeaderString(headers, RabbitMqTopology.FailedAtHeader);
        var failedAt = !string.IsNullOrEmpty(failedAtRaw)
            && DateTime.TryParse(failedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.UtcNow;

        return new DeadLetterMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            RoutingKey = routingKey,
            OriginalQueue = originalQueue,
            Service = service,
            Payload = payload,
            FailureReason = failureReason,
            StackTrace = stackTrace,
            Attempts = attempts,
            FailedAt = failedAt
        };
    }

    private static string? ReadHeaderString(IDictionary<string, object>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value))
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

    private static int? ReadHeaderInt(IDictionary<string, object>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes => int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) ? parsed : null,
            _ => int.TryParse(value.ToString(), out var parsed2) ? parsed2 : null
        };
    }

    private static string? ReadFirstDeathHeader(IDictionary<string, object>? headers, string field)
    {
        if (headers is null || !headers.TryGetValue("x-death", out var value) || value is not List<object> deaths || deaths.Count == 0)
        {
            return null;
        }

        if (deaths[0] is IDictionary<string, object> first && first.TryGetValue(field, out var fieldValue))
        {
            return fieldValue switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string s => s,
                _ => fieldValue.ToString()
            };
        }

        return null;
    }

    private static int? ReadFirstDeathCount(IDictionary<string, object>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-death", out var value) || value is not List<object> deaths || deaths.Count == 0)
        {
            return null;
        }

        if (deaths[0] is IDictionary<string, object> first && first.TryGetValue("count", out var countValue))
        {
            return countValue switch
            {
                long l => (int)l,
                int i => i,
                _ => null
            };
        }

        return null;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _channel?.Dispose();
        return Task.CompletedTask;
    }
}
