using Order.Service.Domain.Abstractions;

namespace Order.Service.Infrastructure.Providers;

internal static class InfrastructureProvidersExtensions
{
    public static IServiceCollection AddInfrastructureProviders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStackExchangeRedisCache(options =>
            options.Configuration = configuration["Redis:Configuration"] ?? "localhost:6379");

        services.AddHttpClient<IProductCatalogClient, HttpProductCatalogClient>(client =>
        {
            var baseUrl = configuration["ProductService:BaseUrl"]
                ?? "http://product-clusterip-service:8080";
            client.BaseAddress = new Uri(baseUrl);
        });

        services.AddScoped<IProductPriceProvider, RedisProductPriceProvider>();

        return services;
    }
}
