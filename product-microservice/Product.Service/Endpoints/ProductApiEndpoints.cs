using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Mvc;
using Product.Service.ApiModels;
using Product.Service.Domain.Abstractions;

namespace Product.Service.Endpoints;

public static class ProductApiEndpoints
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{productId}", async Task<IResult> ([FromServices] IProductStore productStore, int productId) =>
        {
            var product = await productStore.GetById(productId);

            return product is null
                ? TypedResults.NotFound("Product not found")
                : TypedResults.Ok(new GetProductResponse(product.Id, product.Name, product.Price, product.ProductType!.Type, product.Description));
        });

        routeBuilder.MapPost("/", async ([FromServices] IProductStore productStore,
            [FromServices] MetricFactory metricFactory,
            CreateProductRequest request) =>
        {
            var product = new Domain.Product(request.Name, request.Price, request.ProductTypeId, request.Description);

            await productStore.CreateProduct(product);

            metricFactory.Counter("products-created", "products").Add(1);

            return TypedResults.Created(product.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }).RequireAuthorization();

        routeBuilder.MapPut("/{productId}", async Task<IResult> ([FromServices] IProductStore productStore,
            [FromServices] MetricFactory metricFactory,
            int productId, UpdateProductRequest request) =>
        {
            var product = await productStore.GetById(productId);

            if (product is null)
            {
                return TypedResults.NotFound($"Product with id {productId} does not exist");
            }

            var existingPrice = product.Price;

            product.Rename(request.Name);
            product.ChangePrice(request.Price);
            product.ChangeType(request.ProductTypeId);
            product.ChangeDescription(request.Description);

            var priceChanged = !decimal.Equals(existingPrice, request.Price);

            await productStore.UpdateProduct(product);

            if (priceChanged)
            {
                metricFactory.Counter("product-price-updates", "updates").Add(1);
            }

            return TypedResults.NoContent();
        }).RequireAuthorization();
    }
}
