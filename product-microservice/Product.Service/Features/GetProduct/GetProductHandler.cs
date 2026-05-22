using Microsoft.EntityFrameworkCore;
using Product.Service.Infrastructure.Data.EntityFramework;

namespace Product.Service.Features.GetProduct;

internal sealed class GetProductHandler
{
    private readonly ProductContext _context;

    public GetProductHandler(ProductContext context)
    {
        _context = context;
    }

    public Task<GetProductResponse?> HandleAsync(int productId)
    {
        return _context.Products
            .Where(p => p.Id == productId)
            .Select(p => new GetProductResponse(p.Id, p.Name, p.Price, p.ProductType!.Type, p.Description))
            .FirstOrDefaultAsync();
    }
}
