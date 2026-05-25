using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Observability;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public class AzureServiceBusEventBus : IEventBus, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ActivitySource _activitySource;
    private readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;

    public AzureServiceBusEventBus(
        ServiceBusClient client,
        IOptions<AzureServiceBusOptions> options,
        AzureServiceBusTelemetry telemetry)
    {
        _sender = client.CreateSender(options.Value.TopicName);
        _activitySource = telemetry.ActivitySource;
    }

    public async Task PublishAsync(Event @event)
    {
        var eventName = @event.GetType().Name;

        var activityName = $"{OpenTelemetryMessagingConventions.PublishOperation} {eventName}";
        using var activity = _activitySource.StartActivity(activityName, ActivityKind.Producer);

        SetActivityContext(activity, eventName, OpenTelemetryMessagingConventions.PublishOperation);

        var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());

        var message = new ServiceBusMessage(body)
        {
            Subject = eventName,
            ContentType = "application/json",
            MessageId = @event.Id.ToString(),
        };

        var contextToInject = activity?.Context ?? default;
        _propagator.Inject(
            new PropagationContext(contextToInject, Baggage.Current),
            message.ApplicationProperties,
            static (props, key, value) => props[key] = value);

        await _sender.SendMessageAsync(message).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
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
