using Payment.Service.Contracts.Integration;
using Payment.Service.Domain.Events;
using Payment.Service.Features.VoidPaymentCommand;

namespace Payment.Tests.Features.VoidPaymentCommand;

public class PaymentVoidedIntegrationMapTests
{
    [Fact]
    public void Map_PreservesAllFields()
    {
        var domainEvent = new PaymentVoidedDomainEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: "cust-1",
            Reason: "Order cancelled");

        var integrationEvent = new PaymentVoidedIntegrationMap().Map(domainEvent);

        Assert.Equal(domainEvent.PaymentId, integrationEvent.PaymentId);
        Assert.Equal(domainEvent.OrderId, integrationEvent.OrderId);
        Assert.Equal(domainEvent.CustomerId, integrationEvent.CustomerId);
        Assert.Equal(domainEvent.Reason, integrationEvent.Reason);
    }

    [Fact]
    public void DomainEventType_IsPaymentVoidedDomainEvent()
    {
        Assert.Equal(typeof(PaymentVoidedDomainEvent), new PaymentVoidedIntegrationMap().DomainEventType);
    }
}
