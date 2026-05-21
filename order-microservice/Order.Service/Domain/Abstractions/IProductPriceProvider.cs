namespace Order.Service.Domain.Abstractions;

public interface IProductPriceProvider
{
    Task<Dictionary<string, decimal>> GetUnitPricesAsync(IEnumerable<string> productIds);
}
