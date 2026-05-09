using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerce.Shared.Infrastructure.Outbox;

internal sealed class OutboxUnitOfWork : IOutboxUnitOfWork
{
    private readonly IOutboxStore _outboxStore;

    public OutboxUnitOfWork(IOutboxStore outboxStore)
    {
        _outboxStore = outboxStore;
    }

    public Task ExecuteAsync(IExecutionStrategy strategy, Func<Task<IReadOnlyList<Event>>> work)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(work);

        return strategy.ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var events = await work();

            foreach (var @event in events)
            {
                await _outboxStore.AddOutboxEvent(@event);
            }

            scope.Complete();
        });
    }
}
