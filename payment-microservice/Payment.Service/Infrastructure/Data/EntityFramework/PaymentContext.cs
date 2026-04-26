using Microsoft.EntityFrameworkCore;
using Payment.Service.Models;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class PaymentContext : DbContext, IPaymentStore
{
    public PaymentContext(DbContextOptions<PaymentContext> options)
        : base(options)
    {
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
}
