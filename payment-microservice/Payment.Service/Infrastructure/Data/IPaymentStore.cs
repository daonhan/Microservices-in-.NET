using Payment.Service.Models;

namespace Payment.Service.Infrastructure.Data;

public interface IPaymentStore
{
    Task Add(Models.Payment payment);
    Task<Models.Payment?> GetById(Guid paymentId);
    Task<Models.Payment?> GetByOrder(Guid orderId);
    Task<int> SaveChangesAsync();
    Task ExecuteAsync(Func<Task> unitOfWork);
    Task RecordOrderCustomer(Guid orderId, string customerId);
    Task<string?> TryGetOrderCustomer(Guid orderId);
}
