namespace Inventory.Service.ApiModels;

public record RestockResponse(int ProductId, int WarehouseId, int NewOnHand);
