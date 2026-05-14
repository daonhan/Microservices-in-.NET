namespace ECommerce.Shared.Infrastructure.DeadLetter;

internal sealed class AzureServiceBusDeadLetterPublisher : IDeadLetterPublisher
{
    public Guid Publish(DeadLetterReplayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        throw new NotSupportedException("Azure Service Bus dead-letter replay is not implemented yet.");
    }
}
