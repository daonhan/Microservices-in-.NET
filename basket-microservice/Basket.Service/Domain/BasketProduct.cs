namespace Basket.Service.Domain;

internal record BasketProduct(string ProductId, string ProductName, decimal ProductPrice, int Quantity = 1);
