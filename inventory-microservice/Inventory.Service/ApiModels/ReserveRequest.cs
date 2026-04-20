namespace Inventory.Service.ApiModels;

public record ReserveRequest(Guid OrderId, int Quantity);
