using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;

namespace Saga.Tests.Infrastructure.Data.EntityFramework;

public class EfOrderSagaTransitionRunnerTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public EfOrderSagaTransitionRunnerTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_RunningOrderSaga_When_RunAsync_Then_PersistsSnapshotAndTransitionAndEnqueuesCommand()
    {
        var orderId = Guid.NewGuid();
        var sagaId = await SeedOrderSaga(OrderSagaStep.StockReserving, SagaStatus.Running, orderId, amount: 25m);

        var trigger = new StockReservedEvent(orderId, [new ReservedItem(101, 1, 2)], 25m, "USD")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId,
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var runner = scope.ServiceProvider
                .GetRequiredService<ISagaTransitionRunner<OrderSagaStateSnapshot, Event>>();
            await runner.RunAsync(sagaId, trigger, OrderSagaStateMachine.Transition);
        }

        using var assertScope = _factory.Services.CreateScope();
        var sagaContext = assertScope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(OrderSagaStep.PaymentAuthorizing.ToString(), saga.CurrentStep);
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Equal(nameof(AuthorizePaymentCommand), saga.OrderSagaState!.LastStepResult);
        Assert.NotNull(saga.LastCommandId);
        Assert.Contains(saga.Transitions, t =>
            t.FromStep == OrderSagaStep.StockReserving.ToString()
            && t.ToStep == OrderSagaStep.PaymentAuthorizing.ToString()
            && t.TriggerMessageId == trigger.Id
            && t.TriggerKind == SagaTriggerKind.Event);

        var outboxStore = assertScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(unpublished, e =>
            e.Id == saga.LastCommandId
            && e.EventType.Contains(nameof(AuthorizePaymentCommand), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Given_OrderSagaInUnexpectedStep_When_RunAsync_Then_NoPersistenceOrOutboxWrites()
    {
        var orderId = Guid.NewGuid();
        var sagaId = await SeedOrderSaga(OrderSagaStep.Completed, SagaStatus.Completed, orderId);
        var beforeOutbox = await GetUnpublishedCount();

        var trigger = new StockReservedEvent(orderId, [new ReservedItem(101, 1, 2)], 25m, "USD")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId,
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var runner = scope.ServiceProvider
                .GetRequiredService<ISagaTransitionRunner<OrderSagaStateSnapshot, Event>>();
            await runner.RunAsync(sagaId, trigger, OrderSagaStateMachine.Transition);
        }

        using var assertScope = _factory.Services.CreateScope();
        var sagaContext = assertScope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(OrderSagaStep.Completed.ToString(), saga.CurrentStep);
        Assert.Equal(SagaStatus.Completed, saga.Status);
        Assert.Empty(saga.Transitions);

        var afterOutbox = await GetUnpublishedCount();
        Assert.Equal(beforeOutbox, afterOutbox);
    }

    [Fact]
    public async Task Given_RunningOrderSaga_When_BeginCompensation_Then_CompensationStartsAndCommandEnqueued()
    {
        var orderId = Guid.NewGuid();
        var sagaId = await SeedOrderSaga(OrderSagaStep.PaymentAuthorizing, SagaStatus.Running, orderId, amount: 25m);

        var trigger = new PaymentFailedEvent(Guid.NewGuid(), orderId, "customer-1", "Declined")
        {
            CausationId = Guid.NewGuid(),
            SagaId = sagaId,
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOrderSagaTransitionRunner>();
            var outcome = await runner.BeginCompensation(
                sagaId,
                trigger,
                SagaTriggerKind.Timeout,
                "Saga step exceeded max retries; compensation started.");
            Assert.Equal(SagaCompensationOutcomeStatus.Applied, outcome.Status);
        }

        using var assertScope = _factory.Services.CreateScope();
        var sagaContext = assertScope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(SagaStatus.Compensating, saga.Status);
        Assert.Equal(OrderSagaStep.ReleasingStock.ToString(), saga.CurrentStep);
        Assert.Equal(OrderSagaStep.StockReserved.ToString(), saga.OrderSagaState!.CompensationOrigin);
        Assert.NotNull(saga.LastCommandId);
        Assert.Contains(saga.Transitions, t =>
            t.FromStep == OrderSagaStep.PaymentAuthorizing.ToString()
            && t.ToStep == OrderSagaStep.ReleasingStock.ToString()
            && t.TriggerKind == SagaTriggerKind.Timeout);

        var outboxStore = assertScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(unpublished, e =>
            e.Id == saga.LastCommandId
            && e.EventType.Contains(nameof(ReleaseStockCommand), StringComparison.Ordinal));
    }

    private async Task<int> GetUnpublishedCount()
    {
        using var scope = _factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        return unpublished.Count;
    }

    private async Task<Guid> SeedOrderSaga(
        OrderSagaStep step,
        SagaStatus status,
        Guid orderId,
        decimal? amount = null)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var sagaId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var saga = new SagaInstance
        {
            SagaId = sagaId,
            SagaType = "Order",
            CurrentStep = step.ToString(),
            Status = status,
            CorrelationId = Guid.NewGuid(),
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-1),
            OrderSagaState = new OrderSagaState
            {
                SagaId = sagaId,
                OrderId = orderId,
                Amount = amount
            }
        };
        sagaContext.SagaInstances.Add(saga);
        await sagaContext.SaveChangesAsync();
        return sagaId;
    }
}
