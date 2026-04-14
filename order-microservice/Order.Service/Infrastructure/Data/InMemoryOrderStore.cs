namespace Order.Service.Infrastructure.Data;

internal class InMemoryOrderStore : IOrderStore
{
    private static readonly Dictionary<string, Models.Order> Orders = [];

    public Task CreateOrder(Models.Order order)
    {
        Orders[$"{order.CustomerId}-{order.OrderId}"] = order;
        return Task.CompletedTask;
    }

    public Task<Models.Order?> GetCustomerOrderById(string customerId, string orderId) =>
        Task.FromResult(Orders.TryGetValue($"{customerId}-{orderId}", out var order) ? order : null);
}
