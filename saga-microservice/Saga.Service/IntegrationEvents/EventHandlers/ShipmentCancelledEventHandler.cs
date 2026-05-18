using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class ShipmentCancelledEventHandler : IEventHandler<ShipmentCancelledEvent>
{
    private readonly OrderSagaReplyProcessor _processor;
    private readonly RefundSagaReplyProcessor _refundProcessor;

    public ShipmentCancelledEventHandler(
        OrderSagaReplyProcessor processor,
        RefundSagaReplyProcessor refundProcessor)
    {
        _processor = processor;
        _refundProcessor = refundProcessor;
    }

    public async Task Handle(ShipmentCancelledEvent @event)
    {
        await _processor.Handle(@event);
        await _refundProcessor.Handle(@event);
    }
}
