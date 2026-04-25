using Payment.Service.Models;

namespace Payment.Service.Infrastructure.Data;

public interface IPaymentStore
{
    Task<Models.Payment?> GetById(Guid paymentId);
    Task<Models.Payment?> GetByOrder(Guid orderId);
    Task<int> SaveChangesAsync();
}
