namespace Saga.Service.Domain.Abstractions;

internal enum SagaCompensationOutcomeStatus
{
    Applied,
    NotFound,
    UnsupportedSagaType,
    InvalidStatus,
    UnknownCurrentStep,
    NotStarted
}

internal sealed record SagaCompensationOutcome(
    SagaCompensationOutcomeStatus Status,
    Guid SagaId,
    string? CurrentStatus = null,
    string? CurrentStep = null,
    Guid? CommandId = null,
    string? Reason = null);
