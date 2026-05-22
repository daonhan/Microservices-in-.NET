namespace Product.Service.Features.UpdateProduct;

public record UpdateProductRequest(string Name, decimal Price, int ProductTypeId, string? Description = null);
