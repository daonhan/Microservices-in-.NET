using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Order.Service.Domain;
using Order.Service.Domain.Abstractions;
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

        var strategy = Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            await unitOfWork();

            var domainEvents = ChangeTracker.Entries<Entity>()
                .SelectMany(e => e.Entity.DequeueDomainEvents())
                .ToList();

            await SaveChangesAsync(acceptAllChangesOnSuccess: false);

            await _outboxInterceptor.PublishAsync(domainEvents);

            ChangeTracker.AcceptAllChanges();
            scope.Complete();
        });
    }
}
