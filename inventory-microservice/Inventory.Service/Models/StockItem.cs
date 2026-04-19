namespace Inventory.Service.Models;

internal class StockItem
{
    public int ProductId { get; set; }

    public int TotalOnHand { get; set; }

    public int TotalReserved { get; set; }

    public int LowStockThreshold { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public int Available => TotalOnHand - TotalReserved;
}
