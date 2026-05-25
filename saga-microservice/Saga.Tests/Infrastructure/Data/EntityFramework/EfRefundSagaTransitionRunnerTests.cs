using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.RefundSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;

namespace Saga.Tests.Infrastructure.Data.EntityFramework;

public class EfRefundSagaTransitionRunnerTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public EfRefundSagaTransitionRunnerTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_RunningRefundSaga_When_RunAsync_Then_PersistsSnapshotAndTransitionAndEnqueuesCommand()
    {
        var orderId = Guid.NewGuid();
        var sagaId = await SeedRefundSaga(
            RefundSagaStep.PaymentRefunding,
            SagaStatus.Running,
            orderId,
            shipmentId: Guid.NewGuid());

        var trigger = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 42.50m)
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId,
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var runner = scope.ServiceProvider
                .GetRequiredService<ISagaTransitionRunner<RefundSagaStateSnapshot, Event>>();
            await runner.RunAsync(sagaId, trigger, RefundSagaStateMachine.Transition);
        }

        using var assertScope = _factory.Services.CreateScope();
        var sagaContext = assertScope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.RefundSagaState)
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(RefundSagaStep.ShipmentCancellingOrReturning.ToString(), saga.CurrentStep);
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Equal(nameof(CancelShipmentCommand), saga.RefundSagaState!.LastStepResult);
        Assert.NotNull(saga.LastCommandId);
        Assert.Contains(saga.Transitions, t =>
            t.FromStep == RefundSagaStep.PaymentRefunding.ToString()
            && t.ToStep == RefundSagaStep.ShipmentCancellingOrReturning.ToString()
            && t.TriggerMessageId == trigger.Id
            && t.TriggerKind == SagaTriggerKind.Event);

        var outboxStore = assertScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(unpublished, e =>
            e.Id == saga.LastCommandId
            && e.EventType.Contains(nameof(CancelShipmentCommand), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Given_RefundSagaInUnexpectedStep_When_RunAsync_Then_NoPersistenceOrOutboxWrites()
    {
        var orderId = Guid.NewGuid();
        var sagaId = await SeedRefundSaga(
            RefundSagaStep.Completed,
            SagaStatus.Completed,
            orderId,
            shipmentId: null);
        var beforeOutbox = await GetUnpublishedCount();

        var trigger = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 42.50m)
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId,
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var runner = scope.ServiceProvider
                .GetRequiredService<ISagaTransitionRunner<RefundSagaStateSnapshot, Event>>();
            await runner.RunAsync(sagaId, trigger, RefundSagaStateMachine.Transition);
        }

        using var assertScope = _factory.Services.CreateScope();
        var sagaContext = assertScope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(RefundSagaStep.Completed.ToString(), saga.CurrentStep);
        Assert.Equal(SagaStatus.Completed, saga.Status);
        Assert.Empty(saga.Transitions);

        var afterOutbox = await GetUnpublishedCount();
        Assert.Equal(beforeOutbox, afterOutbox);
    }

    [Fact]
    public async Task Given_RunningRefundSagaWithoutShipment_When_RunAsync_Then_SagaCompletesWithoutCommands()
    {
        var orderId = Guid.NewGuid();
        var sagaId = await SeedRefundSaga(
            RefundSagaStep.PaymentRefunding,
            SagaStatus.Running,
            orderId,
            shipmentId: null);
        var beforeOutbox = await GetUnpublishedCount();

        var trigger = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 42.50m)
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId,
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var runner = scope.ServiceProvider
                .GetRequiredService<ISagaTransitionRunner<RefundSagaStateSnapshot, Event>>();
            await runner.RunAsync(sagaId, trigger, RefundSagaStateMachine.Transition);
        }

        using var assertScope = _factory.Services.CreateScope();
        var sagaContext = assertScope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(SagaStatus.Completed, saga.Status);
        Assert.Equal(RefundSagaStep.Completed.ToString(), saga.CurrentStep);
        Assert.Single(saga.Transitions);

        var afterOutbox = await GetUnpublishedCount();
        Assert.Equal(beforeOutbox, afterOutbox);
    }

    private async Task<int> GetUnpublishedCount()
    {
        using var scope = _factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        return unpublished.Count;
    }

    private async Task<Guid> SeedRefundSaga(
        RefundSagaStep step,
        SagaStatus status,
        Guid orderId,
        Guid? shipmentId)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var sagaId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var saga = new SagaInstance
        {
            SagaId = sagaId,
            SagaType = "Refund",
            CurrentStep = step.ToString(),
            Status = status,
            CorrelationId = Guid.NewGuid(),
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-1),
            RefundSagaState = new RefundSagaState
            {
                SagaId = sagaId,
                OrderId = orderId,
                PaymentId = Guid.NewGuid(),
                ShipmentId = shipmentId,
                RefundAmount = 42.50m,
                Currency = "USD"
            }
        };
        sagaContext.SagaInstances.Add(saga);
        await sagaContext.SaveChangesAsync();
        return sagaId;
    }
}
