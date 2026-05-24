using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.RefundSaga;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class RefundSagaReplyProcessor
{
    private readonly ISagaTransitionRunner<RefundSagaStateSnapshot, Event> _runner;

    public RefundSagaReplyProcessor(ISagaTransitionRunner<RefundSagaStateSnapshot, Event> runner)
    {
        _runner = runner;
    }

    public Task Handle(Event @event) =>
        @event.SagaId is { } sagaId
            ? _runner.RunAsync(sagaId, @event, RefundSagaStateMachine.Transition)
            : Task.CompletedTask;
}
