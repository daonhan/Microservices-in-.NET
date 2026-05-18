using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Reaper;
using Saga.Service.Models;
using Saga.Service.StateMachines;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private const string SagaType = "Order";

    private readonly SagaContext _sagaContext;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly SagaOrchestratorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly OrderSagaTimeoutScheduler _timeoutScheduler;

    public OrderCreatedEventHandler(
        SagaContext sagaContext,
        IOutboxUnitOfWork outboxUnitOfWork,
        IOptions<SagaOrchestratorOptions> options,
        TimeProvider timeProvider,
        OrderSagaTimeoutScheduler timeoutScheduler)
    {
        _sagaContext = sagaContext;
        _outboxUnitOfWork = outboxUnitOfWork;
        _options = options.Value;
        _timeProvider = timeProvider;
        _timeoutScheduler = timeoutScheduler;
    }

    public async Task Handle(OrderCreatedEvent @event)
    {
        if (!ShouldOrchestrate(@event.OrderId))
        {
            return;
        }

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

            return result.Commands;
        });
    }

    private bool ShouldOrchestrate(Guid orderId)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        if (_options.AllowList.Contains(orderId))
        {
            return true;
        }

        if (_options.Percentage <= 0)
        {
            return false;
        }

        if (_options.Percentage >= 100)
        {
            return true;
        }

        return GetBucket(orderId) < _options.Percentage;
    }

    internal static int GetBucket(Guid orderId)
    {
        var bytes = orderId.ToByteArray();
        var value = BitConverter.ToUInt32(bytes, 0);
        return (int)(value % 100);
    }
}
