namespace Order.Service.Features.CancelOrder;

public record CancelOrderResponse(Guid OrderId, string CustomerId, DateTime OrderDate, string Status);
