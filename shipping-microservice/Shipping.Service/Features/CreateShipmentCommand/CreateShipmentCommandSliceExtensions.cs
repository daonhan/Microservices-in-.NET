using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.Features.CreateShipmentCommand;

internal static class CreateShipmentCommandSliceExtensions
{
    public static IServiceCollection AddCreateShipmentCommandSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ECommerce.Shared.IntegrationEvents.Commands.CreateShipmentCommand, CreateShipmentCommandHandler>();
        return services;
    }
}
