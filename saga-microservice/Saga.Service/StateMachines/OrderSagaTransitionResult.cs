using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.StateMachines;

internal sealed record OrderSagaTransitionResult(
    OrderSagaStateSnapshot State,
    IReadOnlyList<Event> Commands,
    bool Changed);
