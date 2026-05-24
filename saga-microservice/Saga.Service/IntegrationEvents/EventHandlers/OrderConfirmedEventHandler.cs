using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class OrderConfirmedEventHandler : IEventHandler<OrderConfirmedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public OrderConfirmedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(OrderConfirmedEvent @event) => _processor.Handle(@event);
}
