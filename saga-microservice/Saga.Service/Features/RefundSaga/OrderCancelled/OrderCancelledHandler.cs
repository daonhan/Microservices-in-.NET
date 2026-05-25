using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.RefundSaga;

namespace Saga.Service.Features.RefundSaga.OrderCancelled;

internal sealed class OrderCancelledHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly ISagaTransitionRunner<RefundSagaStateSnapshot, Event> _runner;

    public OrderCancelledHandler(ISagaTransitionRunner<RefundSagaStateSnapshot, Event> runner)
    {
        _runner = runner;
    }

    public Task Handle(OrderCancelledEvent @event) =>
        @event.SagaId is { } sagaId
            ? _runner.RunAsync(sagaId, @event, RefundSagaStateMachine.Transition)
            : Task.CompletedTask;
}
