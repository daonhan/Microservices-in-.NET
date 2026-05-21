using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;
using Order.Service.Infrastructure.Outbox;

namespace Order.Service.Features.CancelOrder;

internal static class CancelOrderSliceExtensions
{
    public static IServiceCollection AddCancelOrderSlice(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationMap, OrderCancelledIntegrationMap>();
        services.AddEventHandler<CancelOrderCommand, CancelOrderCommandHandler>();
        return services;
    }
}
