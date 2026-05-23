using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Infrastructure.Outbox;

namespace Payment.Service.Features.AuthorizePaymentCommand;

internal static class AuthorizePaymentCommandSliceExtensions
{
    public static IServiceCollection AddAuthorizePaymentCommandSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ECommerce.Shared.IntegrationEvents.Commands.AuthorizePaymentCommand, AuthorizePaymentCommandHandler>();
        services.AddScoped<IIntegrationMap, PaymentAuthorizedIntegrationMap>();
        services.AddScoped<IIntegrationMap, PaymentFailedIntegrationMap>();
        return services;
    }
}
