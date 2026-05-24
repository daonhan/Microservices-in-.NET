using Payment.Service.Contracts.Integration;
using Payment.Service.Domain.Events;
using Payment.Service.Features.RefundPayment;

namespace Payment.Tests.Features.RefundPayment;

public class PaymentRefundedIntegrationMapTests
{
    [Fact]
    public void Map_PreservesAllFields()
    {
        var domainEvent = new PaymentRefundedDomainEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            Amount: 9.99m);

        var integrationEvent = new PaymentRefundedIntegrationMap().Map(domainEvent);

        Assert.Equal(domainEvent.PaymentId, integrationEvent.PaymentId);
        Assert.Equal(domainEvent.OrderId, integrationEvent.OrderId);
        Assert.Equal(domainEvent.Amount, integrationEvent.Amount);
    }

    [Fact]
    public void DomainEventType_IsPaymentRefundedDomainEvent()
    {
        Assert.Equal(typeof(PaymentRefundedDomainEvent), new PaymentRefundedIntegrationMap().DomainEventType);
    }
}
