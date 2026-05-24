using Payment.Service.Contracts.Integration;
using Payment.Service.Domain.Events;
using Payment.Service.Features.AuthorizePaymentCommand;

namespace Payment.Tests.Features.AuthorizePaymentCommand;

public class PaymentAuthorizedIntegrationMapTests
{
    [Fact]
    public void Map_PreservesAllFields()
    {
        var domainEvent = new PaymentAuthorizedDomainEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: "cust-1",
            Amount: 42.50m,
            Currency: "USD");

        var integrationEvent = new PaymentAuthorizedIntegrationMap().Map(domainEvent);

        Assert.Equal(domainEvent.PaymentId, integrationEvent.PaymentId);
        Assert.Equal(domainEvent.OrderId, integrationEvent.OrderId);
        Assert.Equal(domainEvent.CustomerId, integrationEvent.CustomerId);
        Assert.Equal(domainEvent.Amount, integrationEvent.Amount);
        Assert.Equal(domainEvent.Currency, integrationEvent.Currency);
    }

    [Fact]
    public void DomainEventType_IsPaymentAuthorizedDomainEvent()
    {
        Assert.Equal(typeof(PaymentAuthorizedDomainEvent), new PaymentAuthorizedIntegrationMap().DomainEventType);
    }
}
