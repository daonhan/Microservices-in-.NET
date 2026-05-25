using Azure.Messaging.ServiceBus;
using ECommerce.Shared.Infrastructure.AzureServiceBus;
using Microsoft.Extensions.Options;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

internal sealed class AzureServiceBusDeadLetterPublisher : IDeadLetterPublisher
{
    internal const string ReplayedFromProperty = "replayed_from";

    private readonly ServiceBusSender _sender;

    public AzureServiceBusDeadLetterPublisher(ServiceBusClient client, IOptions<AzureServiceBusOptions> options)
    {
        _sender = client.CreateSender(options.Value.TopicName);
    }

    public Guid Publish(DeadLetterReplayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var newMessageId = Guid.NewGuid();
        var correlationId = request.CorrelationId ?? request.FailureId;
        var message = new ServiceBusMessage(BinaryData.FromString(request.Payload))
        {
            ContentType = "application/json",
            Subject = request.EventType,
            MessageId = newMessageId.ToString(),
            CorrelationId = correlationId.ToString()
        };

        message.ApplicationProperties[AzureServiceBusHostedService.EventTypeProperty] = request.EventType;
        message.ApplicationProperties[AzureServiceBusHostedService.OriginalQueueProperty] = request.OriginalQueue;
        message.ApplicationProperties[AzureServiceBusHostedService.CorrelationIdProperty] = correlationId.ToString();
        message.ApplicationProperties[ReplayedFromProperty] = request.FailureId.ToString();

        _sender.SendMessageAsync(message).GetAwaiter().GetResult();

        return newMessageId;
    }
}
