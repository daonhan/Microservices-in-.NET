using Order.Service.Domain.Events;
using Order.Service.Features.ConfirmOrder;

namespace Order.Tests.Features.ConfirmOrder;

public class OrderConfirmedIntegrationMapTests
{
    [Fact]
    public void Map_PreservesAllFields()
    {
        var orderId = Guid.NewGuid();
        const string customerId = "cust-42";
        var domainEvent = new OrderConfirmedDomainEvent(orderId, customerId);

        var integrationEvent = new OrderConfirmedIntegrationMap().Map(domainEvent);

        Assert.Equal(orderId, integrationEvent.OrderId);
        Assert.Equal(customerId, integrationEvent.CustomerId);
    }
}
