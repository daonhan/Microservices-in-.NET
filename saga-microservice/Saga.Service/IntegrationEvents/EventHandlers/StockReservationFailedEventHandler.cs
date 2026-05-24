using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class StockReservationFailedEventHandler : IEventHandler<StockReservationFailedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public StockReservationFailedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(StockReservationFailedEvent @event) => _processor.Handle(@event);
}
