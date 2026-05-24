using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;

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
