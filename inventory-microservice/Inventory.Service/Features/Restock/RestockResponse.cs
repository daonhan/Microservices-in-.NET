namespace Inventory.Service.Features.Restock;

public record RestockResponse(int ProductId, int WarehouseId, int NewOnHand);
