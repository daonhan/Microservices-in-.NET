using Basket.Service.Domain;

namespace Basket.Service.Infrastructure.Data;

internal record CustomerBasketCacheModel(List<BasketProduct> Products);
