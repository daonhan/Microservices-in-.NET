namespace Order.Service.Features.GetOrder;

public record GetOrderResponse(Guid OrderId, string CustomerId, DateTime OrderDate, string Status);
