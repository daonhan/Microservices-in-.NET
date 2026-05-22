namespace Product.Service.Features.CreateProduct;

public record CreateProductRequest(string Name, decimal Price, int ProductTypeId, string? Description = null);
