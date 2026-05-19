namespace Saga.Service.ApiModels;

public sealed record SagaListResponse(
    IReadOnlyList<SagaListItemResponse> Items,
    int Total);

public sealed record SagaListItemResponse(
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
    Guid? OrderId);

public sealed record SagaDetailResponse(
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
    OrderSagaStateResponse? Order,
    IReadOnlyList<SagaTransitionResponse> Transitions);

public sealed record OrderSagaStateResponse(
    Guid OrderId,
    Guid? ReservationId,
    Guid? PaymentId,
    Guid? ShipmentId,
    decimal? Amount,
    string? CompensationOrigin,
    string? LastStepResult);

public sealed record SagaTransitionResponse(
    long Id,
    string FromStep,
    string ToStep,
    DateTime Timestamp,
    Guid TriggerMessageId,
    string TriggerKind,
    string? Error);

public sealed record OperatorSagaActionResponse(
    Guid SagaId,
    string Status,
    string CurrentStep,
    Guid? CommandId);
