using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public sealed class DeadLetterDbContext : DbContext, IDeadLetterStore
{
    public DeadLetterDbContext(DbContextOptions<DeadLetterDbContext> options) : base(options)
    {
    }

    public DbSet<DeadLetterMessage> DeadLetterMessages => Set<DeadLetterMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DeadLetterMessageConfiguration());
    }

    public async Task CaptureAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
    {
        DeadLetterMessages.Add(message);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task<DeadLetterPage> ListAsync(DeadLetterFilter filter, CancellationToken cancellationToken = default)
    {
        var query = DeadLetterMessages.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Service))
        {
            query = query.Where(x => x.Service == filter.Service);
        }

        if (!string.IsNullOrWhiteSpace(filter.EventType))
        {
            query = query.Where(x => x.EventType == filter.EventType);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(x => x.Status == filter.Status.Value);
        }

        if (filter.From.HasValue)
        {
            query = query.Where(x => x.FailedAt >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            query = query.Where(x => x.FailedAt <= filter.To.Value);
        }

        if (filter.Origin.HasValue)
        {
            query = query.Where(x => x.Origin == filter.Origin.Value);
        }

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.FailedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new DeadLetterPage(items, page, pageSize, totalCount);
    }

    public Task<DeadLetterMessage?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeadLetterMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> MarkReplayedAsync(Guid id, string replayedBy, CancellationToken cancellationToken = default)
    {
        var entity = await DeadLetterMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null || entity.Status != DeadLetterStatus.Pending)
        {
            return false;
        }

        entity.Status = DeadLetterStatus.Replayed;
        entity.ReplayedAt = DateTime.UtcNow;
        entity.ReplayedBy = replayedBy;
        await SaveChangesAsync(cancellationToken);
        return true;
    }
}
