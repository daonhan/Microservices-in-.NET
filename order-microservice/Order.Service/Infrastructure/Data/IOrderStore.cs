namespace Order.Service.Infrastructure.Data;

internal interface IOrderStore
{
    Task CreateOrder(Models.Order order);
    Task<Models.Order?> GetCustomerOrderById(string customerId, string orderId);
    Task<Models.Order?> GetOrderById(Guid orderId);
    Task Commit();
}
