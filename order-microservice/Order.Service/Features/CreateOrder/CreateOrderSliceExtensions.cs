using Order.Service.Infrastructure.Outbox;

namespace Order.Service.Features.CreateOrder;

internal static class CreateOrderSliceExtensions
{
    public static IServiceCollection AddCreateOrderSlice(this IServiceCollection services)
    {
        services.AddScoped<CreateOrderHandler>();
        services.AddScoped<IIntegrationMap, OrderCreatedIntegrationMap>();
        return services;
    }
}
