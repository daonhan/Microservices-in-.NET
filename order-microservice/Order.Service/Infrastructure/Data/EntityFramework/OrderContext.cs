using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus;
using Microsoft.EntityFrameworkCore;
using Order.Service.Domain;
using Order.Service.Domain.Abstractions;
using Order.Service.Domain.Events;
using Order.Service.Infrastructure.Outbox;

namespace Order.Service.Infrastructure.Data.EntityFramework;

internal class OrderContext : DbContext, IOrderStore
{
    private readonly DomainEventOutboxInterceptor? _outboxInterceptor;

    public OrderContext(DbContextOptions<OrderContext> options)
        : base(options)
    {
    }

    public OrderContext(DbContextOptions<OrderContext> options, DomainEventOutboxInterceptor outboxInterceptor)
        : base(options)
    {
        _outboxInterceptor = outboxInterceptor;
    }

    public DbSet<Domain.Order> Orders { get; set; } = null!;
    public DbSet<Domain.OrderProduct> OrderProducts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderProductConfiguration());
    }

    public Task CreateOrder(Domain.Order order)
    {
        Orders.Add(order);
        return Task.CompletedTask;
    }

    public async Task<Domain.Order?> GetCustomerOrderById(string customerId, string orderId)
    {
        return await Orders
            .Include(o => o.OrderProducts)
            .FirstOrDefaultAsync(o => o.OrderId == Guid.Parse(orderId) && o.CustomerId == customerId);
    }

    public async Task<Domain.Order?> GetOrderById(Guid orderId)
    {
        return await Orders
            .Include(o => o.OrderProducts)
            .FirstOrDefaultAsync(o => o.OrderId == orderId);
    }

    public async Task ExecuteAsync(Func<Task> unitOfWork)
    {
        if (_outboxInterceptor is null)
        {
            throw new InvalidOperationException(
                "OrderContext was constructed without a DomainEventOutboxInterceptor; ExecuteAsync requires the runtime constructor.");
        }

        ArgumentNullException.ThrowIfNull(unitOfWork);
        if (Transaction.Current is not null || Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("ExecuteAsync must own the transaction so events are acknowledged only after commit.");
        }

        var strategy = Database.CreateExecutionStrategy();
        // Prepare the command once. Retry its tracked writes and the same mapped
        // event IDs, rather than replaying arbitrary mutations on tracked entities.
        List<(Entity Entity, IReadOnlyList<IDomainEvent> Events)>? captured = null;
        IReadOnlyList<Event>? integrationEvents = null;
        await strategy.ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            if (integrationEvents is null)
            {
                await unitOfWork();
                captured = ChangeTracker.Entries<Entity>()
                    .Select(e => (e.Entity, (IReadOnlyList<IDomainEvent>)e.Entity.DomainEvents.ToList()))
                    .ToList();
                integrationEvents = _outboxInterceptor.Map(captured.SelectMany(e => e.Events));
            }

            await SaveChangesAsync(acceptAllChangesOnSuccess: false);

            await _outboxInterceptor.PublishMappedAsync(integrationEvents);

            scope.Complete();
        });

        // TransactionScope.Dispose commits and may throw, even after Complete.
        ChangeTracker.AcceptAllChanges();
        foreach (var (entity, events) in captured!)
        {
            entity.RemoveDomainEvents(events);
        }
    }
}
