namespace Order.Service.Infrastructure.Providers;

public interface IProductCatalogClient
{
    Task<decimal?> GetUnitPriceAsync(string productId, CancellationToken cancellationToken = default);
}
