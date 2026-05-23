namespace Inventory.Service.Features.ReserveByHttp;

public record ReserveRequest(Guid OrderId, int Quantity);
