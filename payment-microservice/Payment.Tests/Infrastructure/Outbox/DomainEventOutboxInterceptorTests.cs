using Payment.Service.Domain;
using Payment.Service.Domain.Events;
using Payment.Service.Features.AuthorizePaymentCommand;
using Payment.Service.Infrastructure.Outbox;

namespace Payment.Tests.Infrastructure.Outbox;

public class DomainEventOutboxInterceptorTests
{
    [Fact]
    public void Given_DomainEventWithRegisteredMap_When_Translate_Then_EmitsMappedIntegrationEventWithCorrelation()
    {
        var correlation = new MessageCorrelationContext
        {
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            SagaId = Guid.NewGuid(),
        };
        var interceptor = new DomainEventOutboxInterceptor(
            [new PaymentAuthorizedIntegrationMap()],
            correlation);

        var domainEvent = new PaymentAuthorizedDomainEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: "cust-1",
            Amount: 42.50m,
            Currency: "USD");

        var result = interceptor.Translate([domainEvent]);

        var integrationEvent = Assert.Single(result);
        Assert.Equal(correlation.CorrelationId, integrationEvent.CorrelationId);
        Assert.Equal(correlation.CausationId, integrationEvent.CausationId);
        Assert.Equal(correlation.SagaId, integrationEvent.SagaId);
    }

    [Fact]
    public void Given_UnmappedDomainEventType_When_Translate_Then_ThrowsInvalidOperationExceptionWithDescriptiveWording()
    {
        var interceptor = new DomainEventOutboxInterceptor([], new MessageCorrelationContext());
        var unmapped = new UnmappedDomainEvent();

        var ex = Assert.Throws<InvalidOperationException>(() => interceptor.Translate([unmapped]));
        Assert.Equal(
            $"No integration-event translation registered for domain event {nameof(UnmappedDomainEvent)}",
            ex.Message);
    }

    private sealed record UnmappedDomainEvent : IDomainEvent;
}
