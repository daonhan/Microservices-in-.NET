using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
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

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public class AzureServiceBusHostedService : IHostedService, IAsyncDisposable
{
    private const int FailureReasonMaxLength = 1024;
    private const int StackTraceMaxLength = 16 * 1024;
    private const int DeadLetterDescriptionMaxLength = 4096;
    private const string DeadLetterReason = "HandlerFailed";
    internal const string OriginalQueueProperty = "original_queue";
    internal const string EventTypeProperty = "event_type";
    internal const string ServiceProperty = "service";
    internal const string FailureReasonProperty = "failure_reason";
    internal const string AttemptsProperty = "attempts";
    internal const string FailedAtProperty = "failed_at";
    internal const string CorrelationIdProperty = "correlation_id";
    internal const string StackTraceProperty = "stack_trace";

    private static readonly Action<ILogger, string, string, int, Exception?> LogHandlerFailed =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Error,
            new EventId(2, nameof(LogHandlerFailed)),
            "Handler for {EventType} on subscription {Subscription} failed after {Attempts} attempts; dead-lettering.");

    private readonly IServiceProvider _serviceProvider;
    private readonly EventHandlerRegistration _handlerRegistrations;
    private readonly EventBusOptions _eventBusOptions;
    private readonly AzureServiceBusOptions _serviceBusOptions;
    private readonly ServiceBusClient _client;
    private readonly ActivitySource _activitySource;
    private readonly ILogger<AzureServiceBusHostedService> _logger;
    private readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;
    private readonly ResiliencePipeline _handlerPipeline;

    private ServiceBusProcessor? _processor;

    public AzureServiceBusHostedService(
        IServiceProvider serviceProvider,
        ServiceBusClient client,
        IOptions<EventHandlerRegistration> handlerRegistrations,
        IOptions<EventBusOptions> eventBusOptions,
        IOptions<AzureServiceBusOptions> serviceBusOptions,
        AzureServiceBusTelemetry telemetry,
        ILogger<AzureServiceBusHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _client = client;
        _handlerRegistrations = handlerRegistrations.Value;
        _eventBusOptions = eventBusOptions.Value;
        _serviceBusOptions = serviceBusOptions.Value;
        _activitySource = telemetry.ActivitySource;
        _logger = logger;
        _handlerPipeline = BuildHandlerPipeline(_eventBusOptions.RetryCount);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscription name is derived from the EventBus QueueName so the same config knob
        // selects the per-service subscription when running on Azure Service Bus.
        var subscriptionName = _eventBusOptions.QueueName;
        if (string.IsNullOrWhiteSpace(subscriptionName))
        {
            throw new InvalidOperationException("EventBus:QueueName is required for Azure Service Bus subscriber startup.");
        }

        _processor = _client.CreateProcessor(_serviceBusOptions.TopicName, subscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += OnProcessErrorAsync;

        await _processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
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
        }

        GC.SuppressFinalize(this);
    }

    internal async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;
        var eventName = message.Subject;

        var parentContext = _propagator.Extract(default, message.ApplicationProperties, static (props, key) =>
        {
            if (props.TryGetValue(key, out var value) && value is string str)
            {
                return [str];
            }

            return [];
        });

        var activityName = $"{OpenTelemetryMessagingConventions.ReceiveOperation} {eventName}";
        using var activity = _activitySource.StartActivity(activityName, ActivityKind.Consumer, parentContext.ActivityContext);

        SetActivityContext(activity, eventName ?? string.Empty, OpenTelemetryMessagingConventions.ReceiveOperation);

        var inboundCorrelationId = ReadCorrelationId(message, @event: null);
        if (inboundCorrelationId is not null)
        {
            activity?.SetTag("messaging.correlation_id", inboundCorrelationId.Value.ToString());
        }

        if (string.IsNullOrEmpty(eventName) || !_handlerRegistrations.EventTypes.TryGetValue(eventName, out var eventType))
        {
            await args.CompleteMessageAsync(message, args.CancellationToken).ConfigureAwait(false);
            return;
        }

        var json = message.Body.ToString();
        activity?.SetTag("message", json);

        var @event = JsonSerializer.Deserialize(json, eventType) as Event;
        if (@event is null)
        {
            await args.CompleteMessageAsync(message, args.CancellationToken).ConfigureAwait(false);
            return;
        }

        if (inboundCorrelationId is null && @event.CorrelationId is { } eventCorrelationId)
        {
            activity?.SetTag("messaging.correlation_id", eventCorrelationId.ToString());
        }

        var attempts = 0;
        try
        {
            await _handlerPipeline.ExecuteAsync(async cancellationToken =>
            {
                attempts++;
                using var scope = _serviceProvider.CreateScope();

                foreach (var handler in scope.ServiceProvider.GetKeyedServices<IEventHandler>(eventType))
                {
                    await handler.Handle(@event).ConfigureAwait(false);
                }
            }, args.CancellationToken).ConfigureAwait(false);

            await args.CompleteMessageAsync(message, args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogHandlerFailed(_logger, eventName, _eventBusOptions.QueueName, attempts, ex);

            var properties = BuildDeadLetterProperties(message, @event, eventName, attempts, ex, _eventBusOptions.QueueName);
            await args.DeadLetterMessageAsync(
                message,
                properties,
                DeadLetterReason,
                Truncate($"{ex.GetType().FullName}: {ex.Message}", DeadLetterDescriptionMaxLength),
                args.CancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, object> BuildDeadLetterProperties(
        ServiceBusReceivedMessage message,
        Event @event,
        string eventName,
        int attempts,
        Exception ex,
        string subscriptionName)
    {
        var properties = new Dictionary<string, object>
        {
            [OriginalQueueProperty] = subscriptionName,
            [EventTypeProperty] = eventName,
            [ServiceProperty] = subscriptionName,
            [FailureReasonProperty] = Truncate($"{ex.GetType().FullName}: {ex.Message}", FailureReasonMaxLength),
            [StackTraceProperty] = Truncate(ex.ToString(), StackTraceMaxLength),
            [AttemptsProperty] = attempts,
            [FailedAtProperty] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        var correlationId = ReadCorrelationId(message, @event);
        if (correlationId is not null)
        {
            properties[CorrelationIdProperty] = correlationId.Value.ToString();
        }

        return properties;
    }

    private static Guid? ReadCorrelationId(ServiceBusReceivedMessage message, Event? @event)
    {
        if (message.ApplicationProperties.TryGetValue(CorrelationIdProperty, out var value)
            && TryReadGuid(value, out var fromProperties))
        {
            return fromProperties;
        }

        if (Guid.TryParse(message.CorrelationId, out var fromMessage))
        {
            return fromMessage;
        }

        return @event?.CorrelationId;
    }

    private static bool TryReadGuid(object? value, out Guid result)
    {
        switch (value)
        {
            case Guid guid:
                result = guid;
                return true;
            case string str when Guid.TryParse(str, out var parsed):
                result = parsed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static ResiliencePipeline BuildHandlerPipeline(int retryCount)
    {
        if (retryCount <= 0)
        {
            return new ResiliencePipelineBuilder().Build();
        }

        var retry = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<Exception>(),
            MaxRetryAttempts = retryCount,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(200)
        };

        return new ResiliencePipelineBuilder().AddRetry(retry).Build();
    }

    private static readonly Action<ILogger, ServiceBusErrorSource, Exception?> LogProcessorError =
        LoggerMessage.Define<ServiceBusErrorSource>(
            LogLevel.Error,
            new EventId(1, nameof(OnProcessErrorAsync)),
            "Azure Service Bus processor error in {Source}");

    private Task OnProcessErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorError(_logger, args.ErrorSource, args.Exception);
        return Task.CompletedTask;
    }

    private static void SetActivityContext(Activity? activity, string eventName, string operation)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(OpenTelemetryMessagingConventions.System, "azureservicebus");
        activity.SetTag(OpenTelemetryMessagingConventions.OperationName, operation);
        activity.SetTag(OpenTelemetryMessagingConventions.DestinationName, eventName);
    }
}
