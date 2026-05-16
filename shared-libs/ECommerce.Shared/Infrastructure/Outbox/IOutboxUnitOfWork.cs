using ECommerce.Shared.Infrastructure.EventBus;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerce.Shared.Infrastructure.Outbox;

/// <summary>
/// Caller-facing seam for transactional publishing: runs a unit of work
/// under an ambient transaction, then enqueues the returned Integration Events
/// on the Outbox in the same scope. The execution strategy is resolved internally
/// so callers do not need to depend on <see cref="IOutboxStore"/> at all.
/// This seam is provider-neutral and leaves delivery to the configured EventBus provider
/// (e.g. RabbitMQ or Azure Service Bus) without requiring provider-specific call-site code.
/// </summary>
public interface IOutboxUnitOfWork
{
    /// <summary>
    /// Executes <paramref name="work"/> inside a retrying execution strategy and
    /// ambient <see cref="System.Transactions.TransactionScope"/>, enqueuing the
    /// returned events on the Outbox before committing.
    /// The execution strategy is resolved internally from the Outbox data store.
    /// </summary>
    Task ExecuteAsync(Func<Task<IReadOnlyList<Event>>> work);

    /// <summary>
    /// Overload that accepts an externally-created <see cref="IExecutionStrategy"/>
    /// for callers that own their own DbContext (e.g. PaymentContext).
    /// Prefer the parameterless overload when possible.
    /// </summary>
    Task ExecuteAsync(IExecutionStrategy strategy, Func<Task<IReadOnlyList<Event>>> work);
}
