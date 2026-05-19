using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class PaymentVoidedEventHandler : IEventHandler<PaymentVoidedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public PaymentVoidedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(PaymentVoidedEvent @event) => _processor.Handle(@event);
}
