using System.Text.Json;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerce.Shared.Infrastructure.Outbox;

internal sealed class OutboxContext : DbContext, IOutboxStore
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
        var existingEvent = await OutboxEvents.FindAsync(@event.Id);

        if (existingEvent is not null)
        {
            return;
        }

        OutboxEvents.Add(new OutboxEvent
        {
            Id = @event.Id,
            EventType = @event.GetType().AssemblyQualifiedName!,
            Data = JsonSerializer.Serialize(@event, @event.GetType()),
            Status = OutboxEventStatus.Pending,
            CorrelationId = TryParseTraceIdAsGuid(System.Diagnostics.Activity.Current?.TraceId.ToString())
        });

        await SaveChangesAsync();
    }

    public Task<List<OutboxEvent>> GetUnpublishedOutboxEvents()
        => OutboxEvents
            .Where(o => !o.Sent && o.Status == OutboxEventStatus.Pending)
            .ToListAsync();

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
