using System.Globalization;
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

        await SeedBasketAsync(
            cache,
            basketStore,
            QaPersonas.CustomerHappyId,
            QaPersonas.ProductHappyId,
            QaPersonas.ProductHappyName,
            QaPersonas.ProductHappyPrice,
            QaPersonas.ProductHappyQuantity,
            cancellationToken);

        await SeedBasketAsync(
            cache,
            basketStore,
            QaPersonas.CustomerDeclineId,
            QaPersonas.ProductDeclineId,
            QaPersonas.ProductDeclineName,
            QaPersonas.ProductDeclinePrice,
            QaPersonas.ProductDeclineQuantity,
            cancellationToken);

        await SeedBasketAsync(
            cache,
            basketStore,
            QaPersonas.CustomerCancelId,
            QaPersonas.ProductZeroStockId,
            QaPersonas.ProductZeroStockName,
            QaPersonas.ProductZeroStockPrice,
            QaPersonas.ProductZeroStockQuantity,
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedBasketAsync(
        IDistributedCache cache,
        IBasketStore basketStore,
        Guid customerGuid,
        int productId,
        string productName,
        decimal productPrice,
        int quantity,
        CancellationToken cancellationToken)
    {
        var customerId = customerGuid.ToString();
        var existingBasket = await cache.GetStringAsync(customerId, cancellationToken);

        if (existingBasket is not null)
        {
            return;
        }

        var basket = new CustomerBasket { CustomerId = customerId };
        basket.AddBasketProduct(new BasketProduct(
            productId.ToString(CultureInfo.InvariantCulture),
            productName,
            productPrice,
            quantity));

        await basketStore.CreateCustomerBasket(basket);
    }
}
