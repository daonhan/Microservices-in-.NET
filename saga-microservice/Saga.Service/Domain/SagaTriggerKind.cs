namespace Saga.Service.Domain;

internal enum SagaTriggerKind
{
    Command,
    Event,
    Timeout,
    OperatorAction
}
