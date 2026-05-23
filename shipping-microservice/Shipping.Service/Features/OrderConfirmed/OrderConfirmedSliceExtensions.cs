using ECommerce.Shared.Infrastructure.EventBus;
using Shipping.Service.Contracts.Integration;

namespace Shipping.Service.Features.OrderConfirmed;

internal static class OrderConfirmedSliceExtensions
{
    public static IServiceCollection AddOrderConfirmedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<OrderConfirmedEvent, OrderConfirmedEventHandler>();
        return services;
    }
}
