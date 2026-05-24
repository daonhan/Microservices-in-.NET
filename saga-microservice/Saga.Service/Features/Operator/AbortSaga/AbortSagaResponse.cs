namespace Saga.Service.Features.Operator.AbortSaga;

internal sealed record AbortSagaResponse(
    Guid SagaId,
    string Status,
    string CurrentStep,
    Guid? CommandId);
