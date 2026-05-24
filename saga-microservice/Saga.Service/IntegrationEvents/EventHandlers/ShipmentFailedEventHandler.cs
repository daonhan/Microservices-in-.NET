using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class ShipmentFailedEventHandler : IEventHandler<ShipmentFailedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;
    private readonly RefundSagaReplyProcessor _refundProcessor;

    public ShipmentFailedEventHandler(
        OrderSagaReplyProcessor processor,
        RefundSagaReplyProcessor refundProcessor)
    {
        _processor = processor;
        _refundProcessor = refundProcessor;
    }

    public async Task Handle(ShipmentFailedEvent @event)
    {
        await _processor.Handle(@event);
        await _refundProcessor.Handle(@event);
    }
}
