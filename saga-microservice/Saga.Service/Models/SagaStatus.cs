namespace Saga.Service.Models;

internal enum SagaStatus
{
    Running,
    Completed,
    Failed,
    Compensating,
    Compensated,
    Aborted
}
