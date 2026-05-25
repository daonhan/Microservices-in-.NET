namespace Saga.Service.Features.Operator.RetrySaga;

internal sealed record RetrySagaResponse(
    Guid SagaId,
    string Status,
    string CurrentStep,
    Guid? CommandId);
