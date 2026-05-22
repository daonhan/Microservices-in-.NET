using Basket.Service.Domain;

namespace Basket.Service.Infrastructure.Data.Redis;

internal record CustomerBasketCacheModel(List<BasketProduct> Products);
