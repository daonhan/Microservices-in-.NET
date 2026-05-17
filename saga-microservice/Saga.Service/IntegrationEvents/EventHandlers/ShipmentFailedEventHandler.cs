using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class ShipmentFailedEventHandler : IEventHandler<ShipmentFailedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public ShipmentFailedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(ShipmentFailedEvent @event) => _processor.Handle(@event);
}
