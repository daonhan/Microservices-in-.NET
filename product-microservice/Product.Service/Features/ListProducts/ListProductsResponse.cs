namespace Product.Service.Features.ListProducts;

public record ListProductsResponseItem(int Id, string Name, decimal Price, string ProductType, string? Description);
