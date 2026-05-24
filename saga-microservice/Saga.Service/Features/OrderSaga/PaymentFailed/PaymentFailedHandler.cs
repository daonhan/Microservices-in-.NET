using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.IntegrationEvents.EventHandlers;

namespace Saga.Service.Features.OrderSaga.PaymentFailed;

internal sealed class PaymentFailedHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly ISagaTransitionRunner<OrderSagaStateSnapshot, Event> _runner;
    private readonly RefundSagaReplyProcessor _refundProcessor;

    public PaymentFailedHandler(
        ISagaTransitionRunner<OrderSagaStateSnapshot, Event> runner,
        RefundSagaReplyProcessor refundProcessor)
    {
        _runner = runner;
        _refundProcessor = refundProcessor;
    }

    public async Task Handle(PaymentFailedEvent @event)
    {
        if (@event.SagaId is { } sagaId)
        {
            await _runner.RunAsync(sagaId, @event, OrderSagaStateMachine.Transition);
        }

        await _refundProcessor.Handle(@event);
    }
}
