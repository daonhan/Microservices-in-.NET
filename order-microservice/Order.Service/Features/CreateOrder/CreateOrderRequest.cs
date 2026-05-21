namespace Order.Service.Features.CreateOrder;

public record OrderProductDto(string ProductId, int Quantity);
public record CreateOrderRequest(List<OrderProductDto> OrderProducts);
