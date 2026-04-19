namespace Inventory.Service.Models;

internal class StockLevel
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int WarehouseId { get; set; }

    public int OnHand { get; set; }

    public int Reserved { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public Warehouse? Warehouse { get; set; }
}
