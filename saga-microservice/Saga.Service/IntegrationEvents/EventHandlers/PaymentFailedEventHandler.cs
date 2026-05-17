using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class PaymentFailedEventHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public PaymentFailedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(PaymentFailedEvent @event) => _processor.Handle(@event);
}
