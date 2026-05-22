namespace Product.Service.Infrastructure.Outbox;

internal static class DomainEventOutboxExtensions
{
    public static IServiceCollection AddProductOutbox(this IServiceCollection services)
    {
        services.AddScoped<DomainEventOutboxInterceptor>();
        return services;
    }
}
