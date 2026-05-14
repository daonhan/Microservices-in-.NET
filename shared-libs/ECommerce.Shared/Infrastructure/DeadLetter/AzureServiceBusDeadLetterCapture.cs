using System.Diagnostics.Metrics;
using System.Globalization;
using Azure.Messaging.ServiceBus;
using ECommerce.Shared.Infrastructure.AzureServiceBus;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using ECommerce.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public sealed partial class AzureServiceBusDeadLetterCapture : IDeadLetterCapture, IAsyncDisposable
{
    public const string MeterName = DeadLetterMetrics.MeterName;

    private static readonly Counter<long> CapturedCounter = DeadLetterMetrics.Meter.CreateCounter<long>(
        "dlq_messages_total",
        description: "Number of messages captured into the dead-letter store, tagged with provider, service, and event type.");

    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Failed to persist Azure Service Bus dead-letter message; abandoning so the broker can redeliver.")]
    private static partial void LogCaptureFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Azure Service Bus dead-letter capture is disabled because AzureServiceBus:DeadLetterCaptureSubscription is empty; operator endpoints remain available.")]
    private static partial void LogCaptureDisabled(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Azure Service Bus dead-letter capture could not start for topic {Topic} subscription {Subscription}; operator endpoints remain available.")]
    private static partial void LogStartUnavailable(ILogger logger, string topic, string subscription, Exception ex);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Azure Service Bus dead-letter processor reported an error from {Source}.")]
    private static partial void LogProcessorError(ILogger logger, ServiceBusErrorSource source, Exception ex);

    private readonly IServiceProvider _serviceProvider;
    private readonly ServiceBusClient _client;
    private readonly AzureServiceBusOptions _options;
    private readonly ILogger<AzureServiceBusDeadLetterCapture> _logger;
    private ServiceBusProcessor? _processor;

    public AzureServiceBusDeadLetterCapture(
        IServiceProvider serviceProvider,
        ServiceBusClient client,
        IOptions<AzureServiceBusOptions> options,
        ILogger<AzureServiceBusDeadLetterCapture> logger)
    {
        _serviceProvider = serviceProvider;
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var subscription = _options.DeadLetterCaptureSubscription;
        if (string.IsNullOrWhiteSpace(subscription))
        {
            LogCaptureDisabled(_logger);
            return;
        }

        try
        {
            _processor = _client.CreateProcessor(_options.TopicName, subscription, new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                SubQueue = SubQueue.DeadLetter,
            });

            _processor.ProcessMessageAsync += OnMessageReceivedAsync;
            _processor.ProcessErrorAsync += OnProcessErrorAsync;

            await _processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogStartUnavailable(_logger, _options.TopicName, subscription, ex);
            if (_processor is not null)
            {
                await _processor.DisposeAsync().ConfigureAwait(false);
                _processor = null;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }

        GC.SuppressFinalize(this);
    }

    internal async Task OnMessageReceivedAsync(ProcessMessageEventArgs args)
    {
        var message = BuildMessage(args.Message);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
            await store.CaptureAsync(message, args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCaptureFailed(_logger, ex);
            await args.AbandonMessageAsync(args.Message, propertiesToModify: null, args.CancellationToken).ConfigureAwait(false);
            return;
        }

        CapturedCounter.Add(1,
            new KeyValuePair<string, object?>("provider", MessagingOptions.AzureServiceBusProvider),
            new KeyValuePair<string, object?>("service", message.Service),
            new KeyValuePair<string, object?>("event_type", message.EventType));

        await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
    }

    internal static DeadLetterMessage BuildMessage(ServiceBusReceivedMessage message)
    {
        var properties = message.ApplicationProperties;

        var subject = string.IsNullOrEmpty(message.Subject) ? null : message.Subject;
        var eventType = ReadString(properties, AzureServiceBusHostedService.EventTypeProperty)
            ?? subject
            ?? "unknown";
        var originalQueue = ReadString(properties, AzureServiceBusHostedService.OriginalQueueProperty) ?? string.Empty;
        var service = ReadString(properties, AzureServiceBusHostedService.ServiceProperty) ?? originalQueue;
        var failureReason = ReadString(properties, AzureServiceBusHostedService.FailureReasonProperty)
            ?? message.DeadLetterReason
            ?? "unknown";
        var stackTrace = ReadString(properties, AzureServiceBusHostedService.StackTraceProperty)
            ?? message.DeadLetterErrorDescription;

        var attempts = ReadInt(properties, AzureServiceBusHostedService.AttemptsProperty)
            ?? (message.DeliveryCount > 0 ? message.DeliveryCount : 1);

        var failedAtRaw = ReadString(properties, AzureServiceBusHostedService.FailedAtProperty);
        var failedAt = !string.IsNullOrEmpty(failedAtRaw)
            && DateTime.TryParse(failedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.UtcNow;

        var correlationId = ReadCorrelationId(properties, message.CorrelationId) ?? Guid.NewGuid();

        var payload = message.Body.ToString();

        return new DeadLetterMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            RoutingKey = subject ?? eventType,
            OriginalQueue = originalQueue,
            Service = service,
            Payload = payload,
            FailureReason = failureReason,
            StackTrace = stackTrace,
            Attempts = attempts,
            FailedAt = failedAt,
            CorrelationId = correlationId,
            Origin = DeadLetterOrigin.DeadLetter,
        };
    }

    private static Guid? ReadCorrelationId(IReadOnlyDictionary<string, object> properties, string? messageCorrelationId)
    {
        var raw = ReadString(properties, AzureServiceBusHostedService.CorrelationIdProperty);
        if (Guid.TryParse(raw, out var fromProperties))
        {
            return fromProperties;
        }

        return Guid.TryParse(messageCorrelationId, out var fromMessage) ? fromMessage : null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => value.ToString(),
        };
    }

    private static int? ReadInt(IReadOnlyDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed2) ? parsed2 : null,
        };
    }

    private Task OnProcessErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorError(_logger, args.ErrorSource, args.Exception);
        return Task.CompletedTask;
    }
}
