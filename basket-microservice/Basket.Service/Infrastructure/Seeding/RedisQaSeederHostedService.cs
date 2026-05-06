using Basket.Service.Infrastructure.Data;
using Basket.Service.Models;
using ECommerce.Shared.Qa;
using Microsoft.Extensions.Caching.Distributed;

namespace Basket.Service.Infrastructure.Seeding;

internal sealed class RedisQaSeederHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var basketStore = scope.ServiceProvider.GetRequiredService<IBasketStore>();

        var customerId = QaPersonas.CustomerHappyId.ToString();
        var existingBasket = await cache.GetStringAsync(customerId, cancellationToken);

        if (existingBasket is not null)
        {
            return;
        }

        var basket = new CustomerBasket { CustomerId = customerId };
        basket.AddBasketProduct(new BasketProduct(
            QaPersonas.ProductHappyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            QaPersonas.ProductHappyName,
            QaPersonas.ProductHappyPrice,
            QaPersonas.ProductHappyQuantity));

        await basketStore.CreateCustomerBasket(basket);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
