using Basket.Service.Models;

namespace Basket.Service.Infrastructure.Data;

internal class InMemoryBasketStore : IBasketStore
{
    private static readonly Dictionary<string, CustomerBasket> Baskets = [];

    public Task<CustomerBasket> GetBasketByCustomerId(string customerId) =>
        Task.FromResult(Baskets.TryGetValue(customerId, out var value) ? value : new CustomerBasket { CustomerId = customerId });

    public Task CreateCustomerBasket(CustomerBasket customerBasket)
    {
        Baskets[customerBasket.CustomerId] = customerBasket;
        return Task.CompletedTask;
    }

    public Task UpdateCustomerBasket(CustomerBasket customerBasket)
    {
        if (Baskets.TryGetValue(customerBasket.CustomerId, out _))
        {
            Baskets[customerBasket.CustomerId] = customerBasket;
        }
        else
        {
            Baskets[customerBasket.CustomerId] = customerBasket;
        }
        return Task.CompletedTask;
    }

    public Task DeleteCustomerBasket(string customerId)
    {
        Baskets.Remove(customerId);
        return Task.CompletedTask;
    }
}
