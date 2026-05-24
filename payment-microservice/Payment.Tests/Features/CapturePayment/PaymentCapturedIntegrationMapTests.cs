using Payment.Service.Contracts.Integration;
using Payment.Service.Domain.Events;
using Payment.Service.Features.CapturePayment;

namespace Payment.Tests.Features.CapturePayment;

public class PaymentCapturedIntegrationMapTests
{
    [Fact]
    public void Map_PreservesAllFields()
    {
        var domainEvent = new PaymentCapturedDomainEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            Amount: 75.00m);

        var integrationEvent = new PaymentCapturedIntegrationMap().Map(domainEvent);

        Assert.Equal(domainEvent.PaymentId, integrationEvent.PaymentId);
        Assert.Equal(domainEvent.OrderId, integrationEvent.OrderId);
        Assert.Equal(domainEvent.Amount, integrationEvent.Amount);
    }

    [Fact]
    public void DomainEventType_IsPaymentCapturedDomainEvent()
    {
        Assert.Equal(typeof(PaymentCapturedDomainEvent), new PaymentCapturedIntegrationMap().DomainEventType);
    }
}
