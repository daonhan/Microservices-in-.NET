using Microsoft.EntityFrameworkCore;
using Order.Service.Infrastructure.Data.EntityFramework;

namespace Order.Service.Features.ListOrders;

internal sealed class ListOrdersHandler
{
    private readonly OrderContext _context;

    public ListOrdersHandler(OrderContext context)
    {
        _context = context;
    }

    public async Task<List<ListOrdersResponse>> HandleAsync(string customerId)
    {
        return await _context.Orders
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new ListOrdersResponse(o.OrderId, o.CustomerId, o.OrderDate, o.Status.ToString()))
            .ToListAsync();
    }
}
