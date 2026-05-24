using Payment.Service.Contracts.Integration;
using Payment.Service.Domain.Events;
using Payment.Service.Features.AuthorizePaymentCommand;

namespace Payment.Tests.Features.AuthorizePaymentCommand;

public class PaymentFailedIntegrationMapTests
{
    [Fact]
    public void Map_PreservesAllFields()
    {
        var domainEvent = new PaymentFailedDomainEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: "cust-1",
            Reason: "Card declined by issuer");

        var integrationEvent = new PaymentFailedIntegrationMap().Map(domainEvent);

        Assert.Equal(domainEvent.PaymentId, integrationEvent.PaymentId);
        Assert.Equal(domainEvent.OrderId, integrationEvent.OrderId);
        Assert.Equal(domainEvent.CustomerId, integrationEvent.CustomerId);
        Assert.Equal(domainEvent.Reason, integrationEvent.Reason);
    }

    [Fact]
    public void DomainEventType_IsPaymentFailedDomainEvent()
    {
        Assert.Equal(typeof(PaymentFailedDomainEvent), new PaymentFailedIntegrationMap().DomainEventType);
    }
}
