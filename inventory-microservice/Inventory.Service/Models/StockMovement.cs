namespace Inventory.Service.Models;

internal class StockMovement
{
    public long Id { get; set; }

    public int ProductId { get; set; }

    public int WarehouseId { get; set; }

    public MovementType Type { get; set; }

    public int Quantity { get; set; }

    public DateTime OccurredAt { get; set; }

    public Guid? OrderId { get; set; }

    public string? Reason { get; set; }
}
