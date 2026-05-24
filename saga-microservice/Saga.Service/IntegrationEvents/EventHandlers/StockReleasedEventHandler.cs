using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class StockReleasedEventHandler : IEventHandler<StockReleasedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public StockReleasedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(StockReleasedEvent @event) => _processor.Handle(@event);
}
