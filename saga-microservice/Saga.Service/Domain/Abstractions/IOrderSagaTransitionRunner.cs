using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Domain.OrderSaga;

namespace Saga.Service.Domain.Abstractions;

internal interface IOrderSagaTransitionRunner : ISagaTransitionRunner<OrderSagaStateSnapshot, Event>
{
    Task<SagaCompensationOutcome> BeginCompensation(
        Guid sagaId,
        Event trigger,
        SagaTriggerKind triggerKind,
        string error,
        CancellationToken cancellationToken = default);
}
