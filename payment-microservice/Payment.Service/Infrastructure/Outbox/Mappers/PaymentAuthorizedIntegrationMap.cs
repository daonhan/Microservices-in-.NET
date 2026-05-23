using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Events;

namespace Payment.Service.Infrastructure.Outbox.Mappers;

internal sealed class PaymentAuthorizedIntegrationMap : IIntegrationMap<PaymentAuthorizedDomainEvent, PaymentAuthorizedEvent>
{
    public Type DomainEventType => typeof(PaymentAuthorizedDomainEvent);

    public PaymentAuthorizedEvent Map(PaymentAuthorizedDomainEvent domainEvent) => new(
        domainEvent.PaymentId,
        domainEvent.OrderId,
        domainEvent.CustomerId,
        domainEvent.Amount,
        domainEvent.Currency);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((PaymentAuthorizedDomainEvent)domainEvent);
}
