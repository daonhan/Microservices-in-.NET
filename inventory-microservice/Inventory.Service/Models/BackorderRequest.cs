namespace Inventory.Service.Models;

internal class BackorderRequest
{
    public long Id { get; set; }

    public string CustomerId { get; set; } = null!;

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? FulfilledAt { get; set; }
}
