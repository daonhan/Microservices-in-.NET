using Payment.Service.Models;

namespace Payment.Service.Infrastructure.Data;

public interface IPaymentStore
{
    Task<Models.Payment?> GetById(Guid paymentId);
    Task<Models.Payment?> GetByOrder(Guid orderId);
    Task<int> SaveChangesAsync();
    Task RecordOrderCustomer(Guid orderId, string customerId);
    Task<string?> TryGetOrderCustomer(Guid orderId);
}
