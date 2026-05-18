using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.StateMachines;

internal sealed record RefundSagaTransitionResult(
    RefundSagaStateSnapshot State,
    IReadOnlyList<Event> Commands,
    bool Changed);
