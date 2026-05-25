namespace ECommerce.Shared.Infrastructure.DeadLetter;

/// <summary>
/// Re-publishes a stored dead-letter payload directly to the originating queue via the
/// default exchange, so that only the originating service's existing handler pipeline runs it.
/// Returns the new AMQP message id assigned to the replayed message.
/// </summary>
public interface IDeadLetterPublisher
{
    Guid Publish(DeadLetterReplayRequest request);
}

public sealed record DeadLetterReplayRequest(
    string OriginalQueue,
    string EventType,
    string Payload,
    Guid? CorrelationId,
    Guid FailureId);
