using System.Diagnostics;
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

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public class AzureServiceBusHostedService : IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly EventHandlerRegistration _handlerRegistrations;
    private readonly EventBusOptions _eventBusOptions;
    private readonly AzureServiceBusOptions _serviceBusOptions;
    private readonly ServiceBusClient _client;
    private readonly ActivitySource _activitySource;
    private readonly ILogger<AzureServiceBusHostedService> _logger;
    private readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;

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
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscription name is derived from the EventBus QueueName so the same config knob
        // selects the per-service subscription when running on Azure Service Bus.
        var subscriptionName = _eventBusOptions.QueueName;

        _processor = _client.CreateProcessor(_serviceBusOptions.TopicName, subscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = true,
        });

        _processor.ProcessMessageAsync += OnMessageReceivedAsync;
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

    private async Task OnMessageReceivedAsync(ProcessMessageEventArgs args)
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

        SetActivityContext(activity, eventName, OpenTelemetryMessagingConventions.ReceiveOperation);

        if (string.IsNullOrEmpty(eventName) || !_handlerRegistrations.EventTypes.TryGetValue(eventName, out var eventType))
        {
            return;
        }

        var json = message.Body.ToString();
        activity?.SetTag("message", json);

        var @event = JsonSerializer.Deserialize(json, eventType) as Event;
        if (@event is null)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();

        foreach (var handler in scope.ServiceProvider.GetKeyedServices<IEventHandler>(eventType))
        {
            await handler.Handle(@event).ConfigureAwait(false);
        }
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
