using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.RefundSaga;

namespace Saga.Service.Features.RefundSaga.ShipmentFailed;

internal sealed class ShipmentFailedHandler : IEventHandler<ShipmentFailedEvent>
{
    private readonly ISagaTransitionRunner<RefundSagaStateSnapshot, Event> _runner;

    public ShipmentFailedHandler(ISagaTransitionRunner<RefundSagaStateSnapshot, Event> runner)
    {
        _runner = runner;
    }

    public Task Handle(ShipmentFailedEvent @event) =>
        @event.SagaId is { } sagaId
            ? _runner.RunAsync(sagaId, @event, RefundSagaStateMachine.Transition)
            : Task.CompletedTask;
}
