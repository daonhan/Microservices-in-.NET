namespace ECommerce.Shared.Infrastructure.DeadLetter.Models;

public class DeadLetterMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public string OriginalQueue { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public int Attempts { get; set; }
    public DateTime FailedAt { get; set; }
    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Pending;
    public DateTime? ReplayedAt { get; set; }
    public string? ReplayedBy { get; set; }
    public DateTime? DiscardedAt { get; set; }
    public string? DiscardedBy { get; set; }
    public string? DiscardReason { get; set; }
    public Guid? CorrelationId { get; set; }
    public DeadLetterOrigin Origin { get; set; } = DeadLetterOrigin.DeadLetter;
}

public enum DeadLetterStatus
{
    Pending = 0,
    Replayed = 1,
    Discarded = 2
}
