namespace Order.Service.ApiModels;

public record GetOrderResponse(Guid OrderId, string CustomerId, DateTime OrderDate, string Status);
