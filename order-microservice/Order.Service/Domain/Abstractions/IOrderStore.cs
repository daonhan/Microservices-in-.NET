namespace Order.Service.Domain.Abstractions;

internal interface IOrderStore
{
    Task CreateOrder(Order order);
    Task<Order?> GetCustomerOrderById(string customerId, string orderId);
    Task<Order?> GetOrderById(Guid orderId);
    Task ExecuteAsync(Func<Task> unitOfWork);
}
