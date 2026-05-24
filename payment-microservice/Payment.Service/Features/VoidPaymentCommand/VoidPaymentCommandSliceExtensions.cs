using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Infrastructure.Outbox;

namespace Payment.Service.Features.VoidPaymentCommand;

internal static class VoidPaymentCommandSliceExtensions
{
    public static IServiceCollection AddVoidPaymentCommandSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ECommerce.Shared.IntegrationEvents.Commands.VoidPaymentCommand, VoidPaymentCommandHandler>();
        services.AddScoped<IIntegrationMap, PaymentVoidedIntegrationMap>();
        return services;
    }
}
