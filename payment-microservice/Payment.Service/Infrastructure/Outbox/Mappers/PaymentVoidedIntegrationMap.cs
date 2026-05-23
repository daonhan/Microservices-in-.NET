using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Events;

namespace Payment.Service.Infrastructure.Outbox.Mappers;

internal sealed class PaymentVoidedIntegrationMap : IIntegrationMap<PaymentVoidedDomainEvent, PaymentVoidedEvent>
{
    public Type DomainEventType => typeof(PaymentVoidedDomainEvent);

    public PaymentVoidedEvent Map(PaymentVoidedDomainEvent domainEvent) => new(
        domainEvent.PaymentId,
        domainEvent.OrderId,
        domainEvent.CustomerId,
        domainEvent.Reason);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((PaymentVoidedDomainEvent)domainEvent);
}
