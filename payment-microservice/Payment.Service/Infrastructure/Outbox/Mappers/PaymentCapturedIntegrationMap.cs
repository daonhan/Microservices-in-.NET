using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Events;

namespace Payment.Service.Infrastructure.Outbox.Mappers;

internal sealed class PaymentCapturedIntegrationMap : IIntegrationMap<PaymentCapturedDomainEvent, PaymentCapturedEvent>
{
    public Type DomainEventType => typeof(PaymentCapturedDomainEvent);

    public PaymentCapturedEvent Map(PaymentCapturedDomainEvent domainEvent) => new(
        domainEvent.PaymentId,
        domainEvent.OrderId,
        domainEvent.Amount);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((PaymentCapturedDomainEvent)domainEvent);
}
