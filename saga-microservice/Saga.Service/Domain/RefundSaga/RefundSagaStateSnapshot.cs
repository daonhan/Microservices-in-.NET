namespace Saga.Service.Domain.RefundSaga;

internal sealed record RefundSagaStateSnapshot(
    Guid SagaId,
    Guid OrderId,
    Guid PaymentId,
    Guid? ShipmentId,
    decimal RefundAmount,
    string Currency,
    RefundSagaStep CurrentStep,
    SagaStatus Status,
    string? LastStepResult = null);
