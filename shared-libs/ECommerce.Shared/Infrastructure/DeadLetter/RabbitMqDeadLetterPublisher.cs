using System.Text;
using ECommerce.Shared.Infrastructure.RabbitMq;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

internal sealed class RabbitMqDeadLetterPublisher : IDeadLetterPublisher
{
    private readonly IRabbitMqConnection _connection;

    public RabbitMqDeadLetterPublisher(IRabbitMqConnection connection)
    {
        _connection = connection;
    }

    public Guid Publish(DeadLetterReplayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OriginalQueue))
        {
            throw new InvalidOperationException("Cannot replay a dead-letter message without an OriginalQueue.");
        }

        using var channel = _connection.Connection.CreateModel();

        var properties = channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.DeliveryMode = 2; // persistent

        var newMessageId = Guid.NewGuid();
        properties.MessageId = newMessageId.ToString();

        var correlationId = request.CorrelationId ?? request.FailureId;
        properties.CorrelationId = correlationId.ToString();

        properties.Headers = new Dictionary<string, object>
        {
            [RabbitMqTopology.EventTypeHeader] = Encoding.UTF8.GetBytes(request.EventType),
            [RabbitMqTopology.ReplayedFromHeader] = Encoding.UTF8.GetBytes(request.FailureId.ToString())
        };

        var body = Encoding.UTF8.GetBytes(request.Payload);

        // Default exchange + routingKey = queueName routes the message ONLY to OriginalQueue.
        // The subscriber's x-event-type header fallback dispatches it to the registered handler.
        channel.BasicPublish(
            exchange: string.Empty,
            routingKey: request.OriginalQueue,
            mandatory: false,
            basicProperties: properties,
            body: body);

        return newMessageId;
    }
}
