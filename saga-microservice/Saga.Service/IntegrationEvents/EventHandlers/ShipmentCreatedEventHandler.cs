using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class ShipmentCreatedEventHandler : IEventHandler<ShipmentCreatedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public ShipmentCreatedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(ShipmentCreatedEvent @event) => _processor.Handle(@event);
}
