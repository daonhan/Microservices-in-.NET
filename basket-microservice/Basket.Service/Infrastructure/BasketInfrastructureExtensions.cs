using Basket.Service.Domain.Abstractions;
using Basket.Service.Infrastructure.Data.Redis;

namespace Basket.Service.Infrastructure;

internal static class BasketInfrastructureExtensions
{
    public static IServiceCollection AddBasketInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IBasketStore, RedisBasketStore>();
        return services;
    }
}
