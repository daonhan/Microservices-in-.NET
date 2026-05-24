namespace Saga.Service.Features.Operator.ListSagas;

internal sealed record ListSagasResponse(
    IReadOnlyList<ListSagasItemResponse> Items,
    int Total);

internal sealed record ListSagasItemResponse(
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
