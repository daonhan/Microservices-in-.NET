namespace ECommerce.Shared.Infrastructure.DeadLetter.Models;

/// <summary>
/// Identifies which subsystem produced a row in <c>dead_letter_messages</c>.
/// Existing rows backfill to <see cref="DeadLetter"/>.
/// </summary>
public enum DeadLetterOrigin
{
    /// <summary>RabbitMQ DLX captures from <c>RabbitMqDeadLetterCapture</c>.</summary>
    DeadLetter = 0,

    /// <summary>Outbox publish failures pulled from per-service <c>/internal/outbox/failed</c>.</summary>
    Outbox = 1
}
