using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Models;
using Saga.Service.StateMachines;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class OrderSagaReplyProcessor
{
    private readonly SagaContext _sagaContext;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly TimeProvider _timeProvider;

    public OrderSagaReplyProcessor(
        SagaContext sagaContext,
        IOutboxUnitOfWork outboxUnitOfWork,
        TimeProvider timeProvider)
    {
        _sagaContext = sagaContext;
        _outboxUnitOfWork = outboxUnitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task Handle(Event @event)
    {
        if (@event.SagaId is not { } sagaId)
        {
            return;
        }

        await _outboxUnitOfWork.ExecuteAsync(_sagaContext.Database.CreateExecutionStrategy(), async () =>
        {
            var saga = await _sagaContext.SagaInstances
                .Include(s => s.OrderSagaState)
                .FirstOrDefaultAsync(s => s.SagaId == sagaId);
            if (saga?.OrderSagaState is null)
            {
                return [];
            }

            var currentStep = Enum.Parse<OrderSagaStep>(saga.CurrentStep);
            var snapshot = new OrderSagaStateSnapshot(
                saga.SagaId,
                saga.OrderSagaState.OrderId,
                currentStep,
                saga.Status,
                saga.OrderSagaState.LastStepResult);
            var result = OrderSagaStateMachine.Transition(snapshot, @event);
            if (!result.Changed)
            {
                return [];
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            saga.CurrentStep = result.State.CurrentStep.ToString();
            saga.Status = result.State.Status;
            saga.UpdatedAt = now;
            saga.OrderSagaState.LastStepResult = result.State.LastStepResult;
            saga.Transitions.Add(new SagaTransition
            {
                SagaId = saga.SagaId,
                FromStep = currentStep.ToString(),
                ToStep = result.State.CurrentStep.ToString(),
                Timestamp = now,
                TriggerMessageId = @event.Id,
                TriggerKind = SagaTriggerKind.Event,
                Error = @event is StockReservationFailedEvent ? "Stock reservation failed." : null
            });

            await _sagaContext.SaveChangesAsync();

            return result.Commands;
        });
    }
}
