namespace Saga.Service.Domain.OrderSaga;

internal sealed record OrderSagaStateSnapshot(
    Guid SagaId,
    Guid OrderId,
    OrderSagaStep CurrentStep,
    SagaStatus Status,
    string? LastStepResult = null,
    decimal? Amount = null,
    OrderSagaStep? CompensationOrigin = null);
