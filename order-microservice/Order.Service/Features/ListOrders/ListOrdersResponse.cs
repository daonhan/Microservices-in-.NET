namespace Order.Service.Features.ListOrders;

public record ListOrdersResponse(Guid OrderId, string CustomerId, DateTime OrderDate, string Status);
