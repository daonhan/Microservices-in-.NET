namespace ECommerce.Shared.Infrastructure.EventBus;

public record Event
{
    public Event()
    {
        Id = Guid.NewGuid();
        CreatedDate = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// Optional correlation id used to stitch a logical workflow across services.
    /// When set, the value is propagated through publish, consume, dead-letter capture
    /// and replay, and surfaces in OTEL traces so failures link back to the original flow.
    /// </summary>
    public Guid? CorrelationId { get; set; }
}
