using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.RefundSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Observability;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed partial class RefundRequestedEventHandler : IEventHandler<RefundRequestedEvent>
{
    private const string SagaType = "Refund";

    private readonly SagaContext _sagaContext;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefundRequestedEventHandler> _logger;

    public RefundRequestedEventHandler(
        SagaContext sagaContext,
        IOutboxUnitOfWork outboxUnitOfWork,
        TimeProvider timeProvider,
        ILogger<RefundRequestedEventHandler> logger)
    {
        _sagaContext = sagaContext;
        _outboxUnitOfWork = outboxUnitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(RefundRequestedEvent @event)
    {
        await _outboxUnitOfWork.ExecuteAsync(_sagaContext.Database.CreateExecutionStrategy(), async () =>
        {
            if (await _sagaContext.RefundSagaStates.AnyAsync(s => s.OrderId == @event.OrderId))
            {
                return [];
            }

            var sagaId = Guid.NewGuid();
            var initial = new RefundSagaStateSnapshot(
                sagaId,
                @event.OrderId,
                @event.PaymentId,
                @event.ShipmentId,
                @event.RefundAmount,
                @event.Currency,
                RefundSagaStep.Started,
                SagaStatus.Running);
            var result = RefundSagaStateMachine.Transition(initial, @event);
            if (!result.Changed)
            {
                return [];
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            using var activity = SagaTelemetry.StartTransition(
                sagaId,
                SagaType,
                RefundSagaStep.Started.ToString(),
                result.State.CurrentStep.ToString());
            var saga = new SagaInstance
            {
                SagaId = sagaId,
                SagaType = SagaType,
                CurrentStep = result.State.CurrentStep.ToString(),
                Status = result.State.Status,
                CorrelationId = @event.CorrelationId,
                CreatedAt = now,
                UpdatedAt = now,
                LastCommandId = result.Commands.Count == 0 ? null : result.Commands[0].Id,
                RefundSagaState = new RefundSagaState
                {
                    SagaId = sagaId,
                    OrderId = @event.OrderId,
                    PaymentId = @event.PaymentId,
                    ShipmentId = @event.ShipmentId,
                    RefundAmount = @event.RefundAmount,
                    Currency = @event.Currency,
                    LastStepResult = result.State.LastStepResult
                },
                Transitions =
                {
                    new SagaTransition
                    {
                        SagaId = sagaId,
                        FromStep = RefundSagaStep.Started.ToString(),
                        ToStep = result.State.CurrentStep.ToString(),
                        Timestamp = now,
                        TriggerMessageId = @event.Id,
                        TriggerKind = SagaTriggerKind.Event
                    }
                }
            };

            _sagaContext.SagaInstances.Add(saga);
            await _sagaContext.SaveChangesAsync();

            SagaTelemetry.Started.Add(1, new KeyValuePair<string, object?>("type", SagaType));
            LogRefundSagaStarted(
                _logger,
                sagaId,
                result.State.CurrentStep,
                @event.Id,
                @event.CausationId);

            return result.Commands;
        });
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Refund saga {SagaId} opened at step {Step} (MessageId {MessageId}, CausationId {CausationId})")]
    private static partial void LogRefundSagaStarted(
        ILogger logger,
        Guid sagaId,
        RefundSagaStep step,
        Guid messageId,
        Guid? causationId);
}
