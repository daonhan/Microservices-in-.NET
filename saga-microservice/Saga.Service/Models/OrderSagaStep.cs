namespace Saga.Service.Models;

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
    Completed
}
