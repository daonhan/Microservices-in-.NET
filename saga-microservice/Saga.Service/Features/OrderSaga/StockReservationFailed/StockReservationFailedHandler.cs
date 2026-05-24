using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;

namespace Saga.Service.Features.OrderSaga.StockReservationFailed;

internal sealed class StockReservationFailedHandler : IEventHandler<StockReservationFailedEvent>
{
    private readonly ISagaTransitionRunner<OrderSagaStateSnapshot, Event> _runner;

    public StockReservationFailedHandler(ISagaTransitionRunner<OrderSagaStateSnapshot, Event> runner)
    {
        _runner = runner;
    }

    public Task Handle(StockReservationFailedEvent @event) =>
        @event.SagaId is { } sagaId
            ? _runner.RunAsync(sagaId, @event, OrderSagaStateMachine.Transition)
            : Task.CompletedTask;
}
