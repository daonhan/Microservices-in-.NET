using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Infrastructure.Data.EntityFramework;

namespace Payment.Service.Features.GetPaymentById;

internal sealed class GetPaymentByIdHandler
{
    private const string AdminRole = "Administrator";
    private const string CustomerIdClaim = "customerId";

    private readonly PaymentContext _context;

    public GetPaymentByIdHandler(PaymentContext context)
    {
        _context = context;
    }

    public async Task<PaymentResponse?> HandleAsync(ClaimsPrincipal user, Guid paymentId)
    {
        var response = await _context.Payments
            .Where(p => p.PaymentId == paymentId)
            .Select(p => new PaymentResponse(
                p.PaymentId,
                p.OrderId,
                p.CustomerId,
                p.Amount,
                p.Currency,
                p.Status.ToString(),
                p.ProviderReference,
                p.CreatedAt,
                p.UpdatedAt))
            .FirstOrDefaultAsync();

        if (response is null)
        {
            return null;
        }

        if (!IsAuthorized(user, response.CustomerId))
        {
            return null;
        }

        return response;
    }

    private static bool IsAuthorized(ClaimsPrincipal user, string customerId)
    {
        if (user.HasClaim("user_role", AdminRole))
        {
            return true;
        }

        var callerCustomerId = user.FindFirst(CustomerIdClaim)?.Value;
        return callerCustomerId is not null && callerCustomerId == customerId;
    }
}
