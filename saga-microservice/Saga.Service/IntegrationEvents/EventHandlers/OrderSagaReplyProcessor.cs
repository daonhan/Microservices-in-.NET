using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class OrderSagaReplyProcessor
{
    private readonly ISagaTransitionRunner<OrderSagaStateSnapshot, Event> _runner;

    public OrderSagaReplyProcessor(ISagaTransitionRunner<OrderSagaStateSnapshot, Event> runner)
    {
        _runner = runner;
    }

    public Task Handle(Event @event) =>
        @event.SagaId is { } sagaId
            ? _runner.RunAsync(sagaId, @event, OrderSagaStateMachine.Transition)
            : Task.CompletedTask;
}
