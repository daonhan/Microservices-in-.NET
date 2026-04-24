namespace Shipping.Service.Models;

internal class Shipment
{
    private readonly List<ShipmentLine> _lines = [];

    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public required string CustomerId { get; set; }

    public int WarehouseId { get; set; }

    public ShipmentStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public IReadOnlyCollection<ShipmentLine> Lines => _lines.AsReadOnly();

    public void AddLine(int productId, int quantity)
    {
        _lines.Add(new ShipmentLine
        {
            ProductId = productId,
            Quantity = quantity,
        });
    }
}
