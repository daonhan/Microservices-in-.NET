using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;

namespace Inventory.Service.Features.CommitStock;

internal static class CommitStockSliceExtensions
{
    public static IServiceCollection AddCommitStockSlice(this IServiceCollection services)
    {
        services.AddEventHandler<CommitStockCommand, CommitStockCommandHandler>();
        return services;
    }
}
