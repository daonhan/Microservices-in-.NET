using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class PaymentRefundedEventHandler : IEventHandler<PaymentRefundedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public PaymentRefundedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(PaymentRefundedEvent @event) => _processor.Handle(@event);
}
