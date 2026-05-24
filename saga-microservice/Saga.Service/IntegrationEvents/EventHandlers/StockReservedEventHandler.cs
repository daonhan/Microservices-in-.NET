using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class StockReservedEventHandler : IEventHandler<StockReservedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public StockReservedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(StockReservedEvent @event) => _processor.Handle(@event);
}
