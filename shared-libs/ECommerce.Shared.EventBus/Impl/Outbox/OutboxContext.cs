using System.Text.Json;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerce.Shared.Infrastructure.Outbox;

internal sealed class OutboxContext : DbContext, IOutboxStore, IOutboxDispatchStore
{
    public OutboxContext(DbContextOptions<OutboxContext> options)
        : base(options)
    {
    }

    public DbSet<OutboxEvent> OutboxEvents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxEventConfiguration());
    }

    public async Task AddOutboxEvent<T>(T @event) where T : Event
    {
        // A previous ambient transaction may have rolled back after SaveChanges.
        // The identity map is not evidence that this row committed.
        if (await OutboxEvents.AsNoTracking().AnyAsync(row => row.Id == @event.Id))
        {
            return;
        }

        var tracked = OutboxEvents.Local.FirstOrDefault(row => row.Id == @event.Id);
        if (tracked is not null)
        {
            Entry(tracked).State = EntityState.Detached;
        }

        var row = new OutboxEvent
        {
            Id = @event.Id,
            EventType = @event.GetType().AssemblyQualifiedName!,
            Data = JsonSerializer.Serialize(@event, @event.GetType()),
            Status = OutboxEventStatus.Pending,
            CorrelationId = TryParseTraceIdAsGuid(System.Diagnostics.Activity.Current?.TraceId.ToString())
        };
        OutboxEvents.Add(row);
        try
        {
            await SaveChangesAsync();
        }
        finally
        {
            // Retries must consult the database after ambient rollback.
            Entry(row).State = EntityState.Detached;
        }
    }

    public Task<List<OutboxEvent>> GetUnpublishedOutboxEvents()
        => (Database.IsSqlServer()
                ? OutboxEvents.FromSqlRaw("SELECT * FROM [OutboxEvents] WITH (READPAST, READCOMMITTEDLOCK)")
                : OutboxEvents)
            .AsNoTracking()
            .Where(o => !o.Sent && o.Status == OutboxEventStatus.Pending)
            .OrderBy(o => o.Id)
            .Take(100)
            .ToListAsync();

    public async Task DispatchAsync(Guid id, Func<OutboxEvent, Task> publish, int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // One short-lived transaction per message. SQL releases ownership on
            // rollback/process death; no expiring lease can race a slow publisher.
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var row = await OutboxEvents.FromSqlInterpolated($"""
                    SELECT * FROM [OutboxEvents] WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK)
                    WHERE [Id] = {id} AND [Sent] = 0 AND [Status] = {(int)OutboxEventStatus.Pending}
                    """).SingleOrDefaultAsync(cancellationToken);
                if (row is null)
                {
                    return;
                }

                try
                {
                    await publish(row);
                    cancellationToken.ThrowIfCancellationRequested();
                    row.Sent = true;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    row.Attempts++;
                    row.LastAttemptAt = DateTime.UtcNow;
                    row.LastError = ex.ToString();
                    if (row.Attempts >= maxAttempts)
                    {
                        row.Status = OutboxEventStatus.Failed;
                    }
                }

                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                // A failed commit must not leave Sent=true in the identity map.
                ChangeTracker.Clear();
            }
        });
    }

    public Task<List<OutboxEvent>> GetFailedOutboxEvents()
        => OutboxEvents
            .Where(o => o.Status == OutboxEventStatus.Failed)
            .ToListAsync();

    public async Task MarkOutboxEventAsPublished(Guid outboxEventId)
    {
        var outboxEvent = await OutboxEvents.FindAsync(outboxEventId);

        if (outboxEvent is not null)
        {
            outboxEvent.Sent = true;
            await SaveChangesAsync();
        }
    }

    public async Task<bool> RequeueOutboxEvent(Guid outboxEventId)
    {
        var outboxEvent = await OutboxEvents.FindAsync(outboxEventId);

        if (outboxEvent is null)
        {
            return false;
        }

        outboxEvent.Sent = false;
        outboxEvent.Status = OutboxEventStatus.Pending;
        outboxEvent.Attempts = 0;
        outboxEvent.LastError = null;
        outboxEvent.LastAttemptAt = null;
        await SaveChangesAsync();

        return true;
    }

    public async Task RecordPublishFailure(Guid outboxEventId, string error, int maxAttempts)
    {
        var outboxEvent = await OutboxEvents.FindAsync(outboxEventId);

        if (outboxEvent is null)
        {
            return;
        }

        outboxEvent.Attempts += 1;
        outboxEvent.LastError = error;
        outboxEvent.LastAttemptAt = DateTime.UtcNow;

        if (outboxEvent.Attempts >= maxAttempts)
        {
            outboxEvent.Status = OutboxEventStatus.Failed;
        }

        await SaveChangesAsync();
    }

    public IExecutionStrategy CreateExecutionStrategy() => Database.CreateExecutionStrategy();

    private static Guid? TryParseTraceIdAsGuid(string? traceId)
    {
        if (string.IsNullOrEmpty(traceId) || traceId.Length < 32)
        {
            return null;
        }

        return Guid.TryParseExact(traceId[..32], "N", out var guid) ? guid : null;
    }
}
