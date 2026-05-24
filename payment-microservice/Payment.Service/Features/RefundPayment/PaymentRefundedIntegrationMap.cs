using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Events;
using Payment.Service.Infrastructure.Outbox;

namespace Payment.Service.Features.RefundPayment;

internal sealed class PaymentRefundedIntegrationMap : IIntegrationMap<PaymentRefundedDomainEvent, PaymentRefundedEvent>
{
    public Type DomainEventType => typeof(PaymentRefundedDomainEvent);

    public PaymentRefundedEvent Map(PaymentRefundedDomainEvent domainEvent) => new(
        domainEvent.PaymentId,
        domainEvent.OrderId,
        domainEvent.Amount);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((PaymentRefundedDomainEvent)domainEvent);
}
