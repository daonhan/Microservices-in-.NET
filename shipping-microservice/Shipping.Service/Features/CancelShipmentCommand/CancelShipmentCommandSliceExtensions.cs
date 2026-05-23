using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.Features.CancelShipmentCommand;

internal static class CancelShipmentCommandSliceExtensions
{
    public static IServiceCollection AddCancelShipmentCommandSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ECommerce.Shared.IntegrationEvents.Commands.CancelShipmentCommand, CancelShipmentCommandHandler>();
        return services;
    }
}
