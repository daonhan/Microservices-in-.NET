using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Reaper;
using Saga.Service.Models;
using Saga.Service.Observability;
using Saga.Service.StateMachines;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed partial class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private const string SagaType = "Order";

    private readonly SagaContext _sagaContext;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly OrderSagaTimeoutScheduler _timeoutScheduler;
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(
        SagaContext sagaContext,
        IOutboxUnitOfWork outboxUnitOfWork,
        TimeProvider timeProvider,
        OrderSagaTimeoutScheduler timeoutScheduler,
        ILogger<OrderCreatedEventHandler> logger)
    {
        _sagaContext = sagaContext;
        _outboxUnitOfWork = outboxUnitOfWork;
        _timeProvider = timeProvider;
        _timeoutScheduler = timeoutScheduler;
        _logger = logger;
    }

    public async Task Handle(OrderCreatedEvent @event)
    {
        await _outboxUnitOfWork.ExecuteAsync(_sagaContext.Database.CreateExecutionStrategy(), async () =>
        {
            if (await _sagaContext.OrderSagaStates.AnyAsync(s => s.OrderId == @event.OrderId))
            {
                return [];
            }

            var sagaId = Guid.NewGuid();
            var initial = new OrderSagaStateSnapshot(
                sagaId,
                @event.OrderId,
                OrderSagaStep.Started,
                SagaStatus.Running);
            var result = OrderSagaStateMachine.Transition(initial, @event);
            if (!result.Changed)
            {
                return [];
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            using var activity = SagaTelemetry.StartTransition(
                sagaId,
                SagaType,
                OrderSagaStep.Started.ToString(),
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
                OrderSagaState = new OrderSagaState
                {
                    SagaId = sagaId,
                    OrderId = @event.OrderId,
                    LastStepResult = result.State.LastStepResult
                },
                Transitions =
                {
                    new SagaTransition
                    {
                        SagaId = sagaId,
                        FromStep = OrderSagaStep.Started.ToString(),
                        ToStep = result.State.CurrentStep.ToString(),
                        Timestamp = now,
                        TriggerMessageId = @event.Id,
                        TriggerKind = SagaTriggerKind.Event
                    }
                }
            };

            _timeoutScheduler.Apply(saga, now);

            _sagaContext.SagaInstances.Add(saga);
            await _sagaContext.SaveChangesAsync();

            SagaTelemetry.Started.Add(1, new KeyValuePair<string, object?>("type", SagaType));
            LogSagaStarted(
                _logger,
                sagaId,
                SagaType,
                result.State.CurrentStep,
                @event.Id,
                @event.CausationId);

            return result.Commands;
        });
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Saga {SagaId} ({SagaType}) opened at step {Step} (MessageId {MessageId}, CausationId {CausationId})")]
    private static partial void LogSagaStarted(
        ILogger logger,
        Guid sagaId,
        string sagaType,
        OrderSagaStep step,
        Guid messageId,
        Guid? causationId);
}
