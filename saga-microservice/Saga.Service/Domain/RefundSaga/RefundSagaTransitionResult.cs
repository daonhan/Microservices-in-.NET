using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Domain.RefundSaga;

internal sealed record RefundSagaTransitionResult(
    RefundSagaStateSnapshot State,
    IReadOnlyList<Event> Commands,
    bool Changed);
