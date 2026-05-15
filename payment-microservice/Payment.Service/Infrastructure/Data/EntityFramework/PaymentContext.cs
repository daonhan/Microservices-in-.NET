using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class PaymentContext : DbContext, IPaymentStore
{
    private readonly IOutboxUnitOfWork? _outboxUnitOfWork;

    public PaymentContext(DbContextOptions<PaymentContext> options)
        : base(options)
    {
    }

    public PaymentContext(DbContextOptions<PaymentContext> options, IOutboxUnitOfWork outboxUnitOfWork)
        : base(options)
    {
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public DbSet<Models.Payment> Payments { get; set; } = null!;
    public DbSet<OrderCustomer> OrderCustomers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new OrderCustomerConfiguration());
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
        if (_outboxUnitOfWork is null)
        {
            throw new InvalidOperationException(
                "PaymentContext was constructed without an IOutboxUnitOfWork; ExecuteAsync requires the runtime constructor.");
        }

        var strategy = Database.CreateExecutionStrategy();
        await _outboxUnitOfWork.ExecuteAsync(strategy, async () =>
        {
            await unitOfWork();

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
        PaymentCapturedDomainEvent e => new PaymentCapturedEvent(e.PaymentId, e.OrderId, e.Amount),
        PaymentRefundedDomainEvent e => new PaymentRefundedEvent(e.PaymentId, e.OrderId, e.Amount),
        _ => throw new InvalidOperationException(
            $"No integration-event translation registered for domain event {domainEvent.GetType().Name}")
    };
}
