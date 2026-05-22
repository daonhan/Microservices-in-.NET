using Basket.Service.Domain;

namespace Basket.Service.Domain.Abstractions;

internal interface IBasketStore
{
    Task<CustomerBasket> GetBasketByCustomerId(string customerId);
    Task CreateCustomerBasket(CustomerBasket customerBasket);
    Task UpdateCustomerBasket(CustomerBasket customerBasket);
    Task DeleteCustomerBasket(string customerId);
}
