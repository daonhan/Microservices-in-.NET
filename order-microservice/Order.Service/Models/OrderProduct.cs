namespace Order.Service.Models;

internal class OrderProduct
{
    public int Id { get; set; }
    public required string ProductId { get; init; }
    public int Quantity { get; private set; }
    public Guid OrderId { get; set; }

    public void AddQuantity(int quantity)
    {
        Quantity += quantity;
    }
}
