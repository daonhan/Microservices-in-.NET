namespace Payment.Service.Models;

internal class OrderCustomer
{
    public Guid OrderId { get; set; }

    public required string CustomerId { get; set; }

    public DateTime ReceivedAt { get; set; }
}
