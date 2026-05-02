namespace Order.Service.Models;

internal class Order : Entity
{
    private readonly List<OrderProduct> _orderProducts = [];

    public IReadOnlyCollection<OrderProduct> OrderProducts => _orderProducts.AsReadOnly();

    public required string CustomerId { get; init; }

    public Guid OrderId { get; init; }
    public DateTime OrderDate { get; private set; }

    public OrderStatus Status { get; private set; }

    public Order()
    {
        OrderId = Guid.NewGuid();
        OrderDate = DateTime.UtcNow;
        Status = OrderStatus.PendingStock;
    }

    public void AddOrderProduct(string productId, int quantity)
    {
        var existingOrderForProduct = _orderProducts.SingleOrDefault(o => o.ProductId == productId);

        if (existingOrderForProduct is not null)
        {
            existingOrderForProduct.AddQuantity(quantity);
        }
        else
        {
            var orderProduct = new OrderProduct { ProductId = productId };
            orderProduct.AddQuantity(quantity);

            _orderProducts.Add(orderProduct);
        }
    }

    public bool TryConfirm()
    {
        if (Status != OrderStatus.PendingStock)
        {
            return false;
        }
        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmedDomainEvent(OrderId, CustomerId));
        return true;
    }

    public bool TryCancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }
        Status = OrderStatus.Cancelled;
        Raise(new OrderCancelledDomainEvent(OrderId, CustomerId));
        return true;
    }
}
