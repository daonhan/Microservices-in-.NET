using System.Globalization;

namespace Product.Service.Features.CreateProduct;

internal static class CreateProductEndpoint
{
    public static IEndpointRouteBuilder MapCreateProduct(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/", HandleAsync).RequireAuthorization();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(CreateProductHandler handler, CreateProductRequest request)
    {
        var product = await handler.HandleAsync(request);

        return TypedResults.Created(product.Id.ToString(CultureInfo.InvariantCulture));
    }
}
