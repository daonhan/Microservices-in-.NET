using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;

namespace Inventory.Service.Features.ReserveStock;

internal static class ReserveStockSliceExtensions
{
    public static IServiceCollection AddReserveStockSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ReserveStockCommand, ReserveStockCommandHandler>();
        return services;
    }
}
