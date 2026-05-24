using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class PaymentRefundedEventHandler : IEventHandler<PaymentRefundedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;
    private readonly RefundSagaReplyProcessor _refundProcessor;

    public PaymentRefundedEventHandler(
        OrderSagaReplyProcessor processor,
        RefundSagaReplyProcessor refundProcessor)
    {
        _processor = processor;
        _refundProcessor = refundProcessor;
    }

    public async Task Handle(PaymentRefundedEvent @event)
    {
        await _processor.Handle(@event);
        await _refundProcessor.Handle(@event);
    }
}
