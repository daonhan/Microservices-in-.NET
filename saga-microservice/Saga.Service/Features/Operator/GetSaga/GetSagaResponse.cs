namespace Saga.Service.Features.Operator.GetSaga;

internal sealed record GetSagaResponse(
    Guid SagaId,
    string SagaType,
    string CurrentStep,
    string Status,
    Guid? CorrelationId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? NextTimeoutAt,
    int RetryCount,
    Guid? LastCommandId,
    GetSagaOrderStateResponse? Order,
    IReadOnlyList<GetSagaTransitionResponse> Transitions);

internal sealed record GetSagaOrderStateResponse(
    Guid OrderId,
    Guid? ReservationId,
    Guid? PaymentId,
    Guid? ShipmentId,
    decimal? Amount,
    string? CompensationOrigin,
    string? LastStepResult);

internal sealed record GetSagaTransitionResponse(
    long Id,
    string FromStep,
    string ToStep,
    DateTime Timestamp,
    Guid TriggerMessageId,
    string TriggerKind,
    string? Error);
