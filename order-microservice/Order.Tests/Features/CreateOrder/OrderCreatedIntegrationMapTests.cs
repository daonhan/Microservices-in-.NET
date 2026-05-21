using Order.Service.Domain.Events;
using Order.Service.Features.CreateOrder;

namespace Order.Tests.Features.CreateOrder;

public class OrderCreatedIntegrationMapTests
{
    [Fact]
    public void Map_PreservesAllFields()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-99";
        var domainEvent = new OrderCreatedDomainEvent(
            orderId,
            customerId,
            [new OrderItemSnapshot("p-1", 2, 9.99m), new OrderItemSnapshot("p-2", 1, 4.50m)],
            "EUR");

        var integrationEvent = new OrderCreatedIntegrationMap().Map(domainEvent);

        Assert.Equal(orderId, integrationEvent.OrderId);
        Assert.Equal(customerId, integrationEvent.CustomerId);
        Assert.Equal("EUR", integrationEvent.Currency);
        Assert.Equal(2, integrationEvent.Items.Count);
        Assert.Contains(integrationEvent.Items, i => i.ProductId == "p-1" && i.Quantity == 2 && i.UnitPrice == 9.99m);
        Assert.Contains(integrationEvent.Items, i => i.ProductId == "p-2" && i.Quantity == 1 && i.UnitPrice == 4.50m);
    }
}
