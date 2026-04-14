namespace Basket.Service.ApiModels;

public record AddBasketProductRequest(string ProductId, string ProductName, int Quantity = 1);
