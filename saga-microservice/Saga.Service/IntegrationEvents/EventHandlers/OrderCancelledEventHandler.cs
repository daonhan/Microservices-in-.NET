using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class OrderCancelledEventHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public OrderCancelledEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(OrderCancelledEvent @event) => _processor.Handle(@event);
}
