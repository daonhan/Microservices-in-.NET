using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class PaymentFailedEventHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;
    private readonly RefundSagaReplyProcessor _refundProcessor;

    public PaymentFailedEventHandler(
        OrderSagaReplyProcessor processor,
        RefundSagaReplyProcessor refundProcessor)
    {
        _processor = processor;
        _refundProcessor = refundProcessor;
    }

    public async Task Handle(PaymentFailedEvent @event)
    {
        await _processor.Handle(@event);
        await _refundProcessor.Handle(@event);
    }
}
