namespace Inventory.Service.ApiModels;

public record BackorderRequestDto(string CustomerId, int Quantity);

public record BackorderResponse(long Id, string CustomerId, int ProductId, int Quantity, DateTime CreatedAt);
