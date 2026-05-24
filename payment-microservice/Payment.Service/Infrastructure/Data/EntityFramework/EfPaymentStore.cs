using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal sealed class EfPaymentStore : IPaymentStore
{
    private readonly PaymentContext _ctx;

    public EfPaymentStore(PaymentContext ctx, IOutboxUnitOfWork outboxUnitOfWork)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(outboxUnitOfWork);
        _ctx = ctx;
    }

    public void Add(Domain.Payment payment)
    {
        _ctx.Payments.Add(payment);
    }

    public async Task<Domain.Payment?> GetById(Guid paymentId)
    {
        return await _ctx.Payments.FirstOrDefaultAsync(p => p.PaymentId == paymentId);
    }

    public async Task<Domain.Payment?> GetByOrder(Guid orderId)
    {
        return await _ctx.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
    }

    public Task<int> SaveChangesAsync() => _ctx.SaveChangesAsync();

    public Task ExecuteAsync(Func<Task> unitOfWork) => _ctx.ExecuteAsync(unitOfWork);

    public async Task RecordOrderCustomer(Guid orderId, string customerId)
    {
        var exists = await _ctx.OrderCustomers.AnyAsync(o => o.OrderId == orderId);
        if (exists)
        {
            return;
        }

        _ctx.OrderCustomers.Add(new OrderCustomer
        {
            OrderId = orderId,
            CustomerId = customerId,
            ReceivedAt = DateTime.UtcNow,
        });

        await _ctx.SaveChangesAsync();
    }

    public async Task<string?> TryGetOrderCustomer(Guid orderId)
    {
        var record = await _ctx.OrderCustomers.FirstOrDefaultAsync(o => o.OrderId == orderId);
        return record?.CustomerId;
    }
}
