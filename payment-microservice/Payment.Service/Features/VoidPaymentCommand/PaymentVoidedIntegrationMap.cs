using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Events;
using Payment.Service.Infrastructure.Outbox;

namespace Payment.Service.Features.VoidPaymentCommand;

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
