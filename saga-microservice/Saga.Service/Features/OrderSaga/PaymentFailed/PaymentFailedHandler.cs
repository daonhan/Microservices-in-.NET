using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Domain.RefundSaga;

namespace Saga.Service.Features.OrderSaga.PaymentFailed;

internal sealed class PaymentFailedHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly ISagaTransitionRunner<OrderSagaStateSnapshot, Event> _runner;
    private readonly ISagaTransitionRunner<RefundSagaStateSnapshot, Event> _refundRunner;

    public PaymentFailedHandler(
        ISagaTransitionRunner<OrderSagaStateSnapshot, Event> runner,
        ISagaTransitionRunner<RefundSagaStateSnapshot, Event> refundRunner)
    {
        _runner = runner;
        _refundRunner = refundRunner;
    }

    public async Task Handle(PaymentFailedEvent @event)
    {
        if (@event.SagaId is { } sagaId)
        {
            await _runner.RunAsync(sagaId, @event, OrderSagaStateMachine.Transition);
            await _refundRunner.RunAsync(sagaId, @event, RefundSagaStateMachine.Transition);
        }
    }
}
