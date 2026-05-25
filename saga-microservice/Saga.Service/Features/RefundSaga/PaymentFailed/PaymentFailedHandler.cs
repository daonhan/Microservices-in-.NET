using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.RefundSaga;

namespace Saga.Service.Features.RefundSaga.PaymentFailed;

internal sealed class PaymentFailedHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly ISagaTransitionRunner<RefundSagaStateSnapshot, Event> _runner;

    public PaymentFailedHandler(ISagaTransitionRunner<RefundSagaStateSnapshot, Event> runner)
    {
        _runner = runner;
    }

    public Task Handle(PaymentFailedEvent @event) =>
        @event.SagaId is { } sagaId
            ? _runner.RunAsync(sagaId, @event, RefundSagaStateMachine.Transition)
            : Task.CompletedTask;
}
