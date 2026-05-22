using Product.Service.Infrastructure.Outbox.Mappers;

namespace Product.Service.Infrastructure.Outbox;

internal static class DomainEventOutboxExtensions
{
    public static IServiceCollection AddProductOutbox(this IServiceCollection services)
    {
        services.AddScoped<DomainEventOutboxInterceptor>();
        services.AddScoped<IIntegrationMap, ProductCreatedIntegrationMap>();
        services.AddScoped<IIntegrationMap, ProductPriceUpdatedIntegrationMap>();
        return services;
    }
}
