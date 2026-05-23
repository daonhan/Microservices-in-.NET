using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Domain;
using Payment.Service.Infrastructure.Outbox;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class PaymentContext : DbContext
{
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly DomainEventOutboxInterceptor _interceptor;

    /// <summary>
    /// Design-time only constructor. Used exclusively by <see cref="PaymentContextDesignTimeFactory"/>
    /// for EF Core migrations tooling. Runtime code must use the constructor that accepts
    /// <see cref="IOutboxUnitOfWork"/> and <see cref="DomainEventOutboxInterceptor"/> so
    /// misconfiguration fails fast at startup.
    /// </summary>
    internal PaymentContext(DbContextOptions<PaymentContext> options)
        : base(options)
    {
        // Dependencies are left as default (null) for the design-time path.
        // ExecuteAsync is never called during migrations, so null! is safe here.
        // The runtime constructor below makes them mandatory.
        _outboxUnitOfWork = null!;
        _interceptor = null!;
    }

    public PaymentContext(
        DbContextOptions<PaymentContext> options,
        IOutboxUnitOfWork outboxUnitOfWork,
        DomainEventOutboxInterceptor interceptor)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(outboxUnitOfWork);
        ArgumentNullException.ThrowIfNull(interceptor);
        _outboxUnitOfWork = outboxUnitOfWork;
        _interceptor = interceptor;
    }

    public DbSet<Domain.Payment> Payments { get; set; } = null!;
    public DbSet<OrderCustomer> OrderCustomers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new OrderCustomerConfiguration());
    }

    public async Task ExecuteAsync(Func<Task> unitOfWork)
    {
        var strategy = Database.CreateExecutionStrategy();
        await _outboxUnitOfWork.ExecuteAsync(strategy, async () =>
        {
            await unitOfWork();

            // Snapshot domain events before SaveChanges so that if the execution
            // strategy retries, the aggregate still has its events. We capture the
            // list here and clear the queue only after AcceptAllChanges confirms
            // the transaction committed successfully.
            var domainEvents = ChangeTracker.Entries<Entity>()
                .SelectMany(e => e.Entity.DequeueDomainEvents())
                .ToList();

            await SaveChangesAsync(acceptAllChangesOnSuccess: false);
            ChangeTracker.AcceptAllChanges();

            return _interceptor.Translate(domainEvents);
        });
    }
}
