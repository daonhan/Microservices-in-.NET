using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class StockCommittedEventHandler : IEventHandler<StockCommittedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public StockCommittedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(StockCommittedEvent @event) => _processor.Handle(@event);
}
