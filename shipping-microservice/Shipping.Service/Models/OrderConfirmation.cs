namespace Shipping.Service.Models;

internal class OrderConfirmation
{
    public Guid OrderId { get; set; }

    public required string CustomerId { get; set; }

    public DateTime ReceivedAt { get; set; }
}
