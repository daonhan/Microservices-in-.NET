using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Events;
using Payment.Service.Infrastructure.Outbox;

namespace Payment.Service.Features.AuthorizePaymentCommand;

internal sealed class PaymentFailedIntegrationMap : IIntegrationMap<PaymentFailedDomainEvent, PaymentFailedEvent>
{
    public Type DomainEventType => typeof(PaymentFailedDomainEvent);

    public PaymentFailedEvent Map(PaymentFailedDomainEvent domainEvent) => new(
        domainEvent.PaymentId,
        domainEvent.OrderId,
        domainEvent.CustomerId,
        domainEvent.Reason);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((PaymentFailedDomainEvent)domainEvent);
}
