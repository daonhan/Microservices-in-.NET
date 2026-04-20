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

    public Task<Models.Order?> GetOrderById(Guid orderId) =>
        Task.FromResult(Orders.Values.FirstOrDefault(o => o.OrderId == orderId));

    public Task Commit() => Task.CompletedTask;
}
