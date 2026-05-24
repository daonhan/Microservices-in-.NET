namespace Saga.Service.Domain;

internal enum SagaStatus
{
    Running,
    Completed,
    Failed,
    Compensating,
    Compensated,
    Aborted
}
