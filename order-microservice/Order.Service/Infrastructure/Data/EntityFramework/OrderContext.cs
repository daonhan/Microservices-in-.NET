using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Service.Infrastructure.Data.EntityFramework;

internal class OrderContext : DbContext, IOrderStore
{
    private readonly IOutboxStore? _outboxStore;

    public OrderContext(DbContextOptions<OrderContext> options)
        : base(options)
    {
    }

    public OrderContext(DbContextOptions<OrderContext> options, IOutboxStore outboxStore)
        : base(options)
    {
        _outboxStore = outboxStore;
    }

    public DbSet<Models.Order> Orders { get; set; } = null!;
    public DbSet<Models.OrderProduct> OrderProducts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderProductConfiguration());
    }

    public async Task CreateOrder(Models.Order order)
    {
        Orders.Add(order);
        await SaveChangesAsync(acceptAllChangesOnSuccess: false);
    }

    public async Task<Models.Order?> GetCustomerOrderById(string customerId, string orderId)
    {
        return await Orders
            .Include(o => o.OrderProducts)
            .FirstOrDefaultAsync(o => o.OrderId == Guid.Parse(orderId) && o.CustomerId == customerId);
    }

    public async Task<Models.Order?> GetOrderById(Guid orderId)
    {
        return await Orders
            .Include(o => o.OrderProducts)
            .FirstOrDefaultAsync(o => o.OrderId == orderId);
    }

    public async Task ExecuteAsync(Func<Task> unitOfWork)
    {
        if (_outboxStore is null)
        {
            throw new InvalidOperationException(
                "OrderContext was constructed without an IOutboxStore; ExecuteAsync requires the runtime constructor.");
        }

        var strategy = Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            await unitOfWork();

            var domainEvents = ChangeTracker.Entries<Entity>()
                .SelectMany(e => e.Entity.DequeueDomainEvents())
                .ToList();

            await SaveChangesAsync(acceptAllChangesOnSuccess: false);

            foreach (var domainEvent in domainEvents)
            {
                await _outboxStore.AddOutboxEvent(Translate(domainEvent));
            }

            ChangeTracker.AcceptAllChanges();
            scope.Complete();
        });
    }

    private static Event Translate(IDomainEvent domainEvent) => domainEvent switch
    {
        OrderConfirmedDomainEvent e => new OrderConfirmedEvent(e.OrderId, e.CustomerId),
        OrderCancelledDomainEvent e => new OrderCancelledEvent(e.OrderId, e.CustomerId),
        _ => throw new InvalidOperationException(
            $"No integration-event translation registered for domain event {domainEvent.GetType().Name}")
    };
}
