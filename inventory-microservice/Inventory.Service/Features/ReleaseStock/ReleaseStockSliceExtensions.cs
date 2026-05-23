using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;

namespace Inventory.Service.Features.ReleaseStock;

internal static class ReleaseStockSliceExtensions
{
    public static IServiceCollection AddReleaseStockSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ReleaseStockCommand, ReleaseStockCommandHandler>();
        return services;
    }
}
