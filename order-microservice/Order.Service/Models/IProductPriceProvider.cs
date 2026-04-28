namespace Order.Service.Models;

public interface IProductPriceProvider
{
    Task<Dictionary<string, decimal>> GetUnitPricesAsync(IEnumerable<string> productIds);
}
