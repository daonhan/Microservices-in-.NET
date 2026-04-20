namespace Inventory.Service.Models;

internal class StockReservation
{
    public long Id { get; set; }

    public Guid OrderId { get; set; }

    public int ProductId { get; set; }

    public int WarehouseId { get; set; }

    public int Quantity { get; set; }

    public ReservationStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}
