using Saga.Service.Domain.OrderSaga;
using Saga.Service.Domain.RefundSaga;

namespace Saga.Service.Domain;

internal sealed class SagaInstance
{
    public Guid SagaId { get; set; }

    public required string SagaType { get; set; }

    public required string CurrentStep { get; set; }

    public SagaStatus Status { get; set; }

    public Guid? CorrelationId { get; set; }

    public byte[] Version { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? NextTimeoutAt { get; set; }

    public int RetryCount { get; set; }

    public Guid? LastCommandId { get; set; }

    public OrderSagaState? OrderSagaState { get; set; }

    public RefundSagaState? RefundSagaState { get; set; }

    public List<SagaTransition> Transitions { get; } = [];
}
