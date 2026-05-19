namespace Saga.Service.Models;

internal sealed class RefundSagaState
{
    public Guid SagaId { get; set; }

    public Guid OrderId { get; set; }

    public Guid PaymentId { get; set; }

    public Guid? ShipmentId { get; set; }

    public decimal RefundAmount { get; set; }

    public string Currency { get; set; } = "USD";

    public string? LastStepResult { get; set; }

    public SagaInstance SagaInstance { get; set; } = null!;
}
