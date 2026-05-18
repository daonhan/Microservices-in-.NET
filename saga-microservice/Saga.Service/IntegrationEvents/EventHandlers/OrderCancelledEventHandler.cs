using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class OrderCancelledEventHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly OrderSagaReplyProcessor _processor;
    private readonly RefundSagaReplyProcessor _refundProcessor;

    public OrderCancelledEventHandler(
        OrderSagaReplyProcessor processor,
        RefundSagaReplyProcessor refundProcessor)
    {
        _processor = processor;
        _refundProcessor = refundProcessor;
    }

    public async Task Handle(OrderCancelledEvent @event)
    {
        await _processor.Handle(@event);
        await _refundProcessor.Handle(@event);
    }
}
