namespace Saga.Service.Models;

internal sealed class OrderSagaState
{
    public Guid SagaId { get; set; }

    public Guid OrderId { get; set; }

    public Guid? ReservationId { get; set; }

    public Guid? PaymentId { get; set; }

    public Guid? ShipmentId { get; set; }

    public string? LastStepResult { get; set; }

    public SagaInstance SagaInstance { get; set; } = null!;
}
