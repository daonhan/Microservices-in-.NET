using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerce.Shared.Infrastructure.Outbox;

/// <summary>
/// Low-level primitive for Outbox state management and execution strategy creation.
/// For business logic and transactional publishing, use <see cref="IOutboxUnitOfWork"/> instead.
/// </summary>
public interface IOutboxStore
{
    Task AddOutboxEvent<T>(T @event) where T : Event;
    Task<List<OutboxEvent>> GetUnpublishedOutboxEvents();
    Task MarkOutboxEventAsPublished(Guid outboxEventId);
    Task<bool> RequeueOutboxEvent(Guid outboxEventId);
    Task RecordPublishFailure(Guid outboxEventId, string error, int maxAttempts);
    Task<List<OutboxEvent>> GetFailedOutboxEvents();
    IExecutionStrategy CreateExecutionStrategy();
}
