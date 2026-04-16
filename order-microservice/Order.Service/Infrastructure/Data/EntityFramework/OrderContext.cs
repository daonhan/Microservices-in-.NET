using Microsoft.EntityFrameworkCore;

namespace Order.Service.Infrastructure.Data.EntityFramework;

internal class OrderContext : DbContext, IOrderStore
{
    public OrderContext(DbContextOptions<OrderContext> options)
        : base(options)
    {
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
}
