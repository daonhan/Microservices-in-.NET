using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Reaper;
using Saga.Service.IntegrationEvents;
using Saga.Service.IntegrationEvents.EventHandlers;
using Saga.Service.Models;

namespace Saga.Tests.Domain;

public class SagaReaperServiceTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public SagaReaperServiceTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_OrderCreatedEntersTimedStep_When_HandlerRuns_Then_TimeoutAndLastCommandAreStored()
    {
        var now = new DateTimeOffset(2026, 5, 17, 7, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var timeout = TimeSpan.FromMinutes(1);
        var orderCreated = CreateOrderCreated();

        using (var scope = _factory.Services.CreateScope())
        {
            var handler = new OrderCreatedEventHandler(
                scope.ServiceProvider.GetRequiredService<SagaContext>(),
                scope.ServiceProvider.GetRequiredService<IOutboxUnitOfWork>(),
                timeProvider,
                new OrderSagaTimeoutScheduler(Options.Create(new OrderSagaTimeoutOptions
                {
                    StockReservingTimeout = timeout
                })),
                NullLogger<OrderCreatedEventHandler>.Instance);

            await handler.Handle(orderCreated);
        }

        using var assertScope = _factory.Services.CreateScope();
        var sagaContext = assertScope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .SingleAsync(s => s.OrderSagaState!.OrderId == orderCreated.OrderId);
        Assert.Equal(OrderSagaStep.StockReserving.ToString(), saga.CurrentStep);
        Assert.Equal(now.UtcDateTime.Add(timeout), saga.NextTimeoutAt);
        Assert.Equal(0, saga.RetryCount);
        Assert.NotNull(saga.LastCommandId);

        var outboxStore = assertScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(unpublished, e => e.Id == saga.LastCommandId);
    }

    [Fact]
    public async Task Given_RunningSagaPastNextTimeout_When_ReaperRuns_Then_LastCommandIsRequeuedAndTimeoutMovesForward()
    {
        var now = new DateTimeOffset(2026, 5, 17, 8, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var timeout = TimeSpan.FromMinutes(1);
        var command = await AddPublishedReserveCommand();
        var sagaId = await AddSaga(
            command,
            SagaStatus.Running,
            OrderSagaStep.StockReserving,
            now.UtcDateTime.AddSeconds(-1),
            retryCount: 0);
        var reaper = CreateReaper(timeProvider, timeout, maxRetries: 3);

        await reaper.RunOnceAsync(CancellationToken.None);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Equal(1, saga.RetryCount);
        Assert.Equal(now.UtcDateTime.Add(timeout), saga.NextTimeoutAt);
        Assert.Contains(saga.Transitions, t =>
            t.FromStep == OrderSagaStep.StockReserving.ToString()
            && t.ToStep == OrderSagaStep.StockReserving.ToString()
            && t.TriggerKind == SagaTriggerKind.Timeout);

        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(unpublished, e => e.Id == command.Id);
    }

    [Fact]
    public async Task Given_RunningSagaAtMaxRetries_When_ReaperRuns_Then_CompensationStartsAndCancelOrderIsEnqueued()
    {
        var now = new DateTimeOffset(2026, 5, 17, 9, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var timeout = TimeSpan.FromMinutes(1);
        var command = await AddPublishedReserveCommand();
        var sagaId = await AddSaga(
            command,
            SagaStatus.Running,
            OrderSagaStep.StockReserving,
            now.UtcDateTime.AddSeconds(-1),
            retryCount: 1);
        var reaper = CreateReaper(timeProvider, timeout, maxRetries: 1);

        await reaper.RunOnceAsync(CancellationToken.None);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(SagaStatus.Compensating, saga.Status);
        Assert.Equal(OrderSagaStep.CancellingOrder.ToString(), saga.CurrentStep);
        Assert.NotNull(saga.LastCommandId);
        Assert.NotEqual(command.Id, saga.LastCommandId);
        Assert.Contains(saga.Transitions, t =>
            t.FromStep == OrderSagaStep.StockReserving.ToString()
            && t.ToStep == OrderSagaStep.CancellingOrder.ToString()
            && t.TriggerKind == SagaTriggerKind.Timeout);

        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(unpublished, e =>
            e.Id == saga.LastCommandId
            && e.EventType.Contains(nameof(CancelOrderCommand), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(SagaStatus.Completed))]
    [InlineData(nameof(SagaStatus.Failed))]
    [InlineData(nameof(SagaStatus.Compensated))]
    public async Task Given_NonRunningSagaPastNextTimeout_When_ReaperRuns_Then_SagaIsIgnored(string statusName)
    {
        var status = Enum.Parse<SagaStatus>(statusName);
        var now = new DateTimeOffset(2026, 5, 17, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var command = await AddPublishedReserveCommand();
        var sagaId = await AddSaga(
            command,
            status,
            OrderSagaStep.StockReserving,
            now.UtcDateTime.AddSeconds(-1),
            retryCount: 0);
        var reaper = CreateReaper(timeProvider, TimeSpan.FromMinutes(1), maxRetries: 1);

        await reaper.RunOnceAsync(CancellationToken.None);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(status, saga.Status);
        Assert.Equal(0, saga.RetryCount);
        Assert.Empty(saga.Transitions);

        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.DoesNotContain(unpublished, e => e.Id == command.Id);
    }

    private SagaReaperService CreateReaper(
        TimeProvider timeProvider,
        TimeSpan stockReservingTimeout,
        int maxRetries) =>
        new(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SagaReaperOptions
            {
                IntervalInSeconds = 30,
                MaxRetries = maxRetries
            }),
            new OrderSagaTimeoutScheduler(Options.Create(new OrderSagaTimeoutOptions
            {
                StockReservingTimeout = stockReservingTimeout
            })),
            timeProvider,
            NullLogger<SagaReaperService>.Instance);

    private async Task<ReserveStockCommand> AddPublishedReserveCommand()
    {
        using var scope = _factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var command = new ReserveStockCommand(
            Guid.NewGuid(),
            "customer-1",
            [new ReserveStockItem("101", 2, 12.50m)],
            "USD",
            Guid.NewGuid(),
            Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid()
        };

        await outboxStore.AddOutboxEvent(command);
        await outboxStore.MarkOutboxEventAsPublished(command.Id);
        return command;
    }

    private static OrderCreatedEvent CreateOrderCreated() =>
        new(Guid.NewGuid(), "customer-1", [new OrderItem("101", 2, 12.50m)], "USD")
        {
            CorrelationId = Guid.NewGuid()
        };

    private async Task<Guid> AddSaga(
        ReserveStockCommand command,
        SagaStatus status,
        OrderSagaStep currentStep,
        DateTime nextTimeoutAt,
        int retryCount)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var now = nextTimeoutAt.AddMinutes(-1);
        var saga = new SagaInstance
        {
            SagaId = command.SagaId!.Value,
            SagaType = "Order",
            CurrentStep = currentStep.ToString(),
            Status = status,
            CorrelationId = command.CorrelationId,
            CreatedAt = now,
            UpdatedAt = now,
            NextTimeoutAt = nextTimeoutAt,
            RetryCount = retryCount,
            LastCommandId = command.Id,
            OrderSagaState = new OrderSagaState
            {
                SagaId = command.SagaId.Value,
                OrderId = command.OrderId,
                LastStepResult = nameof(ReserveStockCommand)
            }
        };

        sagaContext.SagaInstances.Add(saga);
        await sagaContext.SaveChangesAsync();
        return saga.SagaId;
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
