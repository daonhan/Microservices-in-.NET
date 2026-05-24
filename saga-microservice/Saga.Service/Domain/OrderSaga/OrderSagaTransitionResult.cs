using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Domain.OrderSaga;

internal sealed record OrderSagaTransitionResult(
    OrderSagaStateSnapshot State,
    IReadOnlyList<Event> Commands,
    bool Changed);
