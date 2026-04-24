namespace Shipping.Service.Models;

internal class ShipmentLine
{
    public int Id { get; set; }

    public Guid ShipmentId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }
}
