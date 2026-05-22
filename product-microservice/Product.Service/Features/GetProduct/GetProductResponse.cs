namespace Product.Service.Features.GetProduct;

public record GetProductResponse(int Id, string Name, decimal Price, string ProductType, string? Description = null);
