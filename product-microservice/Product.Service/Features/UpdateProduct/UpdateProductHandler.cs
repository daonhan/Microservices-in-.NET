using ECommerce.Shared.Observability.Metrics;
using Product.Service.Domain.Abstractions;

namespace Product.Service.Features.UpdateProduct;

internal sealed class UpdateProductHandler
{
    private readonly IProductStore _productStore;
    private readonly MetricFactory _metricFactory;

    public UpdateProductHandler(IProductStore productStore, MetricFactory metricFactory)
    {
        _productStore = productStore;
        _metricFactory = metricFactory;
    }

    public async Task<bool> HandleAsync(int productId, UpdateProductRequest request)
    {
        var product = await _productStore.GetById(productId);

        if (product is null)
        {
            return false;
        }

        var existingPrice = product.Price;

        product.Rename(request.Name);
        product.ChangePrice(request.Price);
        product.ChangeType(request.ProductTypeId);
        product.ChangeDescription(request.Description);

        var priceChanged = !decimal.Equals(existingPrice, request.Price);

        await _productStore.UpdateProduct(product);

        if (priceChanged)
        {
            _metricFactory.Counter("product-price-updates", "updates").Add(1);
        }

        return true;
    }
}
