using Order.Service.Domain.Events;
using Order.Service.Features.CancelOrder;

namespace Order.Tests.Features.CancelOrder;

public class OrderCancelledIntegrationMapTests
{
    [Fact]
    public void Map_PreservesAllFields()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-42";
        var domainEvent = new OrderCancelledDomainEvent(orderId, customerId);

        var integrationEvent = new OrderCancelledIntegrationMap().Map(domainEvent);

        Assert.Equal(orderId, integrationEvent.OrderId);
        Assert.Equal(customerId, integrationEvent.CustomerId);
    }
}
