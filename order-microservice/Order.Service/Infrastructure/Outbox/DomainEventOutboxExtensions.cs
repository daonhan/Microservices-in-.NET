namespace Order.Service.Infrastructure.Outbox;

internal static class DomainEventOutboxExtensions
{
    public static IServiceCollection AddDomainEventOutbox(this IServiceCollection services)
    {
        services.AddScoped<DomainEventOutboxInterceptor>();
        return services;
    }
}
