using Microsoft.EntityFrameworkCore;
using Order.Service.Infrastructure.Data.EntityFramework;

namespace Order.Service.Features.GetOrder;

internal sealed class GetOrderHandler
{
    private readonly OrderContext _context;

    public GetOrderHandler(OrderContext context)
    {
        _context = context;
    }

    public async Task<GetOrderResponse?> HandleAsync(string customerId, string orderId)
    {
        if (!Guid.TryParse(orderId, out var orderGuid))
        {
            return null;
        }

        return await _context.Orders
            .Where(o => o.OrderId == orderGuid && o.CustomerId == customerId)
            .Select(o => new GetOrderResponse(o.OrderId, o.CustomerId, o.OrderDate, o.Status.ToString()))
            .FirstOrDefaultAsync();
    }
}
