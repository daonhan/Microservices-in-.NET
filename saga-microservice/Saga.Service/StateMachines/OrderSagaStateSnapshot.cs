using Saga.Service.Models;

namespace Saga.Service.StateMachines;

internal sealed record OrderSagaStateSnapshot(
    Guid SagaId,
    Guid OrderId,
    OrderSagaStep CurrentStep,
    SagaStatus Status,
    string? LastStepResult = null);
