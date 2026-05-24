namespace Saga.Service.Domain.RefundSaga;

internal enum RefundSagaStep
{
    Started,
    PaymentRefunding,
    PaymentRefunded,
    ShipmentCancellingOrReturning,
    Completed,
    CancellingOrder,
    Compensated
}
