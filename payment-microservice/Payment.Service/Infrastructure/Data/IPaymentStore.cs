using Payment.Service.Domain;

namespace Payment.Service.Infrastructure.Data;

public interface IPaymentStore
{
    void Add(Domain.Payment payment);
    Task<Domain.Payment?> GetById(Guid paymentId);
    Task<Domain.Payment?> GetByOrder(Guid orderId);
    Task<int> SaveChangesAsync();
    Task ExecuteAsync(Func<Task> unitOfWork);
    Task RecordOrderCustomer(Guid orderId, string customerId);
    Task<string?> TryGetOrderCustomer(Guid orderId);
}
