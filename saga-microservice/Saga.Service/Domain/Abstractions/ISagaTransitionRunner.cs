using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Domain.Abstractions;

internal interface ISagaTransitionRunner<TState, TEvent>
    where TEvent : Event
{
    Task RunAsync(
        Guid sagaId,
        TEvent trigger,
        Func<TState, TEvent, TransitionResult<TState>> transitionFn,
        CancellationToken cancellationToken = default);
}
