using ECommerce.Shared.Infrastructure.EventBus;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerce.Shared.Infrastructure.Outbox;

/// <summary>
/// Caller-facing seam for transactional publishing: runs a unit of work
/// under the supplied execution strategy and ambient transaction, then
/// enqueues the returned Integration Events on the Outbox in the same scope.
/// </summary>
public interface IOutboxUnitOfWork
{
    Task ExecuteAsync(IExecutionStrategy strategy, Func<Task<IReadOnlyList<Event>>> work);
}
