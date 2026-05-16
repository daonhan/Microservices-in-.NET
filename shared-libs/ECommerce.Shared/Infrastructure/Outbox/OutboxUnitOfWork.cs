using System.Diagnostics;
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
            using var activity = OutboxTelemetry.ActivitySource.StartActivity("outbox.uow", ActivityKind.Internal);
            activity?.SetTag("outbox.operation", "execute");

            try
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                var events = await work();
                activity?.SetTag("outbox.event_count", events.Count);

                foreach (var @event in events)
                {
                    await _outboxStore.AddOutboxEvent(@event);
                }

                scope.Complete();
                RecordOutcome(activity, "committed");
            }
            catch (Exception ex)
            {
                activity?.SetTag("error.type", ex.GetType().FullName);
                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                RecordOutcome(activity, "rolled_back");
                throw;
            }
        });
    }

    private static void RecordOutcome(Activity? activity, string outcome)
    {
        activity?.SetTag("outbox.outcome", outcome);
        OutboxTelemetry.Transactions.Add(1,
            new KeyValuePair<string, object?>("operation", "execute"),
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}
