namespace Shipping.Service.Models;

internal class ShipmentStatusHistoryEntry
{
    public int Id { get; set; }

    public Guid ShipmentId { get; set; }

    public ShipmentStatus Status { get; set; }

    public DateTime OccurredAt { get; set; }

    public ShipmentStatusSource Source { get; set; }

    public string? Reason { get; set; }
}
