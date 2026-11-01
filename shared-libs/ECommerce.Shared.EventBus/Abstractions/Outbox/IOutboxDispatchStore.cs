using ECommerce.Shared.Infrastructure.Outbox.Models;

namespace ECommerce.Shared.Infrastructure.Outbox;

/// <summary>
/// Optional store capability that holds exclusive ownership through publish and
/// acknowledgement. Existing custom IOutboxStore implementations remain supported.
/// </summary>
public interface IOutboxDispatchStore
{
    Task DispatchAsync(Guid id, Func<OutboxEvent, Task> publish, int maxAttempts,
        CancellationToken cancellationToken = default);
}
