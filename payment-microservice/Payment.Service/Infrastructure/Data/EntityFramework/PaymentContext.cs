using Microsoft.EntityFrameworkCore;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class PaymentContext : DbContext, IPaymentStore
{
    public PaymentContext(DbContextOptions<PaymentContext> options)
        : base(options)
    {
    }

    public DbSet<Models.Payment> Payments { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
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
}
