using Microsoft.EntityFrameworkCore;
using Product.Service.Infrastructure.Data.EntityFramework;

namespace Product.Service.Features.ListProducts;

internal sealed class ListProductsHandler
{
    private readonly ProductContext _context;

    public ListProductsHandler(ProductContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ListProductsResponseItem>> HandleAsync()
    {
        return await _context.Products
            .Select(p => new ListProductsResponseItem(p.Id, p.Name, p.Price, p.ProductType!.Type, p.Description))
            .ToListAsync();
    }
}
