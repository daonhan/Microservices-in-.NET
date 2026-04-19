namespace Inventory.Service.ApiModels;

public record StockItemSummaryDto(
    int ProductId,
    int TotalOnHand,
    int TotalReserved,
    int Available,
    int Threshold);
