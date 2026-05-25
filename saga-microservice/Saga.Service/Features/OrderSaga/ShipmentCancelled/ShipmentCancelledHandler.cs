using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Domain.RefundSaga;

namespace Saga.Service.Features.OrderSaga.ShipmentCancelled;

internal sealed class ShipmentCancelledHandler : IEventHandler<ShipmentCancelledEvent>
{
    private readonly ISagaTransitionRunner<OrderSagaStateSnapshot, Event> _runner;
    private readonly ISagaTransitionRunner<RefundSagaStateSnapshot, Event> _refundRunner;

    public ShipmentCancelledHandler(
        ISagaTransitionRunner<OrderSagaStateSnapshot, Event> runner,
        ISagaTransitionRunner<RefundSagaStateSnapshot, Event> refundRunner)
    {
        _runner = runner;
        _refundRunner = refundRunner;
    }

    public async Task Handle(ShipmentCancelledEvent @event)
    {
        if (@event.SagaId is { } sagaId)
        {
            await _runner.RunAsync(sagaId, @event, OrderSagaStateMachine.Transition);
            await _refundRunner.RunAsync(sagaId, @event, RefundSagaStateMachine.Transition);
        }
    }
}
