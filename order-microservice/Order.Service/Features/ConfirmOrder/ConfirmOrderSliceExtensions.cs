using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;
using Order.Service.Infrastructure.Outbox;

namespace Order.Service.Features.ConfirmOrder;

internal static class ConfirmOrderSliceExtensions
{
    public static IServiceCollection AddConfirmOrderSlice(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationMap, OrderConfirmedIntegrationMap>();
        services.AddEventHandler<ConfirmOrderCommand, ConfirmOrderCommandHandler>();
        return services;
    }
}
