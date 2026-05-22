using ECommerce.Shared.Observability.Metrics;
using Product.Service.Domain.Abstractions;

namespace Product.Service.Features.CreateProduct;

internal sealed class CreateProductHandler
{
    private readonly IProductStore _productStore;
    private readonly MetricFactory _metricFactory;

    public CreateProductHandler(IProductStore productStore, MetricFactory metricFactory)
    {
        _productStore = productStore;
        _metricFactory = metricFactory;
    }

    public async Task<Domain.Product> HandleAsync(CreateProductRequest request)
    {
        var product = new Domain.Product(request.Name, request.Price, request.ProductTypeId, request.Description);

        await _productStore.CreateProduct(product);

        _metricFactory.Counter("products-created", "products").Add(1);

        return product;
    }
}
