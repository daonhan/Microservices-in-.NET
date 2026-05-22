namespace Basket.Service.Features.AddBasketProduct;

public record AddBasketProductRequest(string ProductId, string ProductName, int Quantity = 1);
