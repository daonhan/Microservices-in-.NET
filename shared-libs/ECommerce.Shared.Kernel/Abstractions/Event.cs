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

    /// <summary>
    /// Optional id of the message that caused this event.
    /// Reply events set this to the triggering command id.
    /// </summary>
    public Guid? CausationId { get; set; }

    /// <summary>
    /// Optional saga instance id for messages owned by an orchestrated saga.
    /// </summary>
    public Guid? SagaId { get; set; }
}

public abstract record Command : Event
{
    protected Command(Guid causationId, Guid sagaId)
    {
        if (causationId == Guid.Empty)
        {
            throw new ArgumentException("Command causation id cannot be empty.", nameof(causationId));
        }

        if (sagaId == Guid.Empty)
        {
            throw new ArgumentException("Command saga id cannot be empty.", nameof(sagaId));
        }

        CausationId = causationId;
        SagaId = sagaId;
    }
}
