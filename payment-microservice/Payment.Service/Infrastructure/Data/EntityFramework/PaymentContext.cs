using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class PaymentContext : DbContext, IPaymentStore
{
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    /// <summary>
    /// Design-time only constructor. Used exclusively by <see cref="PaymentContextDesignTimeFactory"/>
    /// for EF Core migrations tooling. Runtime code must use the constructor that accepts
    /// <see cref="IOutboxUnitOfWork"/> so misconfiguration fails fast at startup.
    /// </summary>
    internal PaymentContext(DbContextOptions<PaymentContext> options)
        : base(options)
    {
        // _outboxUnitOfWork is left as default (null) for the design-time path.
        // The Translate/ExecuteAsync methods are never called during migrations,
        // so null! is safe here. The runtime constructor below makes it mandatory.
        _outboxUnitOfWork = null!;
    }

    public PaymentContext(DbContextOptions<PaymentContext> options, IOutboxUnitOfWork outboxUnitOfWork)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(outboxUnitOfWork);
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public DbSet<Models.Payment> Payments { get; set; } = null!;
    public DbSet<OrderCustomer> OrderCustomers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new OrderCustomerConfiguration());
    }

    public void Add(Models.Payment payment)
    {
        Payments.Add(payment);
    }

    public async Task<Models.Payment?> GetById(Guid paymentId)
    {
        return await Payments.FirstOrDefaultAsync(p => p.PaymentId == paymentId);
    }

    public async Task<Models.Payment?> GetByOrder(Guid orderId)
    {
        return await Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
    }

    public Task<int> SaveChangesAsync() => base.SaveChangesAsync();

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

            return domainEvents.Select(Translate).ToList();
        });
    }

    public async Task RecordOrderCustomer(Guid orderId, string customerId)
    {
        var exists = await OrderCustomers.AnyAsync(o => o.OrderId == orderId);
        if (exists)
        {
            return;
        }

        OrderCustomers.Add(new OrderCustomer
        {
            OrderId = orderId,
            CustomerId = customerId,
            ReceivedAt = DateTime.UtcNow,
        });

        await SaveChangesAsync();
    }

    public async Task<string?> TryGetOrderCustomer(Guid orderId)
    {
        var record = await OrderCustomers.FirstOrDefaultAsync(o => o.OrderId == orderId);
        return record?.CustomerId;
    }

    private static Event Translate(IDomainEvent domainEvent) => domainEvent switch
    {
        PaymentAuthorizedDomainEvent e => new PaymentAuthorizedEvent(
            e.PaymentId, e.OrderId, e.CustomerId, e.Amount, e.Currency),
        PaymentFailedDomainEvent e => new PaymentFailedEvent(
            e.PaymentId, e.OrderId, e.CustomerId, e.Reason),
        PaymentCapturedDomainEvent e => new PaymentCapturedEvent(e.PaymentId, e.OrderId, e.Amount),
        PaymentRefundedDomainEvent e => new PaymentRefundedEvent(e.PaymentId, e.OrderId, e.Amount),
        PaymentVoidedDomainEvent e => new PaymentVoidedEvent(
            e.PaymentId, e.OrderId, e.CustomerId, e.Reason),
        _ => throw new InvalidOperationException(
            $"No integration-event translation registered for domain event {domainEvent.GetType().Name}")
    };
}
