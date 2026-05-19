using Saga.Service.Models;

namespace Saga.Service.StateMachines;

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
