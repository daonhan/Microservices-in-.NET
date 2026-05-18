namespace Saga.Service.Models;

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
