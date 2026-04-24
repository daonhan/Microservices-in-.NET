namespace Shipping.Service.Models;

public enum ShipmentStatus
{
    Pending = 0,
    Picked = 1,
    Packed = 2,
    Shipped = 3,
    InTransit = 4,
    Delivered = 5,
    Cancelled = 6,
    Failed = 7,
    Returned = 8,
}
