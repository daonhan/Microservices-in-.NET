namespace Product.Service.Domain.Abstractions;

internal interface IProductStore
{
    Task<Product?> GetById(int id);

    Task CreateProduct(Product product);

    Task UpdateProduct(Product product);
}
