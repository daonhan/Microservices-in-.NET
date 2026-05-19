using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class PaymentAuthorizedEventHandler : IEventHandler<PaymentAuthorizedEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public PaymentAuthorizedEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(PaymentAuthorizedEvent @event) => _processor.Handle(@event);
}
