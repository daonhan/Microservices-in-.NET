using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Domain.Abstractions;

internal sealed record TransitionResult<TState>(
    TState State,
    IReadOnlyList<Event> Commands,
    bool Changed);
