using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.RefundSaga;

namespace Saga.Service.Features.RefundSaga.PaymentRefunded;

internal sealed class PaymentRefundedHandler : IEventHandler<PaymentRefundedEvent>
{
    private readonly ISagaTransitionRunner<RefundSagaStateSnapshot, Event> _runner;

    public PaymentRefundedHandler(ISagaTransitionRunner<RefundSagaStateSnapshot, Event> runner)
    {
        _runner = runner;
    }

    public Task Handle(PaymentRefundedEvent @event) =>
        @event.SagaId is { } sagaId
            ? _runner.RunAsync(sagaId, @event, RefundSagaStateMachine.Transition)
            : Task.CompletedTask;
}
