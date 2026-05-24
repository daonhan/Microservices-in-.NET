namespace Saga.Service.Domain;

internal sealed class SagaTransition
{
    public long Id { get; set; }

    public Guid SagaId { get; set; }

    public required string FromStep { get; set; }

    public required string ToStep { get; set; }

    public DateTime Timestamp { get; set; }

    public Guid TriggerMessageId { get; set; }

    public SagaTriggerKind TriggerKind { get; set; }

    public string? Error { get; set; }

    public SagaInstance SagaInstance { get; set; } = null!;
}
