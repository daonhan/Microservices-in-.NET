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

    /// <inheritdoc />
    public Task ExecuteAsync(Func<Task<IReadOnlyList<Event>>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var strategy = _outboxStore.CreateExecutionStrategy();
        return ExecuteCore(strategy, work);
    }

    /// <inheritdoc />
    public Task ExecuteAsync(IExecutionStrategy strategy, Func<Task<IReadOnlyList<Event>>> work)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(work);

        return ExecuteCore(strategy, work);
    }

    private async Task ExecuteCore(IExecutionStrategy strategy, Func<Task<IReadOnlyList<Event>>> work)
    {
        // Telemetry is recorded outside the execution-strategy delegate so that
        // retries do not inflate the activity/metric counts.
        using var activity = OutboxTelemetry.ActivitySource.StartActivity("outbox.uow", ActivityKind.Internal);
        activity?.SetTag("outbox.operation", "execute");

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                var events = await work();
                activity?.SetTag("outbox.event_count", events.Count);

                foreach (var @event in events)
                {
                    await _outboxStore.AddOutboxEvent(@event);
                }

                scope.Complete();
            });

            RecordOutcome(activity, "committed");
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation is not a failure of the outbox transaction;
            // tag with a distinct outcome so it does not pollute failure-rate signals.
            RecordOutcome(activity, "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            RecordOutcome(activity, "rolled_back");
            throw;
        }
    }

    private static void RecordOutcome(Activity? activity, string outcome)
    {
        activity?.SetTag("outbox.outcome", outcome);
        OutboxTelemetry.Transactions.Add(1,
            new KeyValuePair<string, object?>("operation", "execute"),
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}
