namespace Saga.Service.Domain.OrderSaga;

internal sealed class OrderSagaTimeoutOptions
{
    public TimeSpan? StockReservingTimeout { get; set; }

    public TimeSpan? PaymentAuthorizingTimeout { get; set; }

    public TimeSpan? OrderConfirmingTimeout { get; set; }

    public TimeSpan? StockCommittingTimeout { get; set; }

    public TimeSpan? ShipmentCreatingTimeout { get; set; }
}
