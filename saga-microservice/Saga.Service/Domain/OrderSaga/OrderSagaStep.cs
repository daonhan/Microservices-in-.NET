namespace Saga.Service.Domain.OrderSaga;

internal enum OrderSagaStep
{
    Started,
    StockReserving,
    StockReserved,
    PaymentAuthorizing,
    PaymentAuthorized,
    OrderConfirming,
    OrderConfirmed,
    StockCommitting,
    StockCommitted,
    ShipmentCreating,
    ShipmentCreated,
    Completed,
    VoidingPayment,
    ReleasingStock,
    RefundingPayment,
    CancellingShipment,
    CancellingOrder,
    Compensated
}
