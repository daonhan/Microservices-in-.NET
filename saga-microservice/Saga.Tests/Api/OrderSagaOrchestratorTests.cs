using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.IntegrationEvents;
using Saga.Service.IntegrationEvents.EventHandlers;
using Saga.Service.Models;

namespace Saga.Tests.Api;

public class OrderSagaOrchestratorTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public OrderSagaOrchestratorTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_FlagOff_When_OrderCreated_Then_OrchestratorNoOps()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        await Handle(orderCreated, new SagaOrchestratorOptions { Enabled = false });

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        Assert.False(await sagaContext.OrderSagaStates.AnyAsync(s => s.OrderId == orderId));
    }

    [Fact]
    public async Task Given_OrderInAllowList_When_OrderCreated_Then_SagaOpensAndReserveCommandIsQueued()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        await Handle(orderCreated, new SagaOrchestratorOptions
        {
            Enabled = true,
            AllowList = [orderId]
        });

        await AssertSagaOpened(orderCreated);
    }

    [Fact]
    public async Task Given_OrderInPercentageBucket_When_OrderCreated_Then_SagaOpens()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        await Handle(orderCreated, new SagaOrchestratorOptions
        {
            Enabled = true,
            Percentage = 100
        });

        await AssertSagaOpened(orderCreated);
    }

    [Fact]
    public async Task Given_OrderExcluded_When_OrderCreated_Then_OrchestratorNoOps()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        await Handle(orderCreated, new SagaOrchestratorOptions
        {
            Enabled = true,
            Percentage = 0
        });

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        Assert.False(await sagaContext.OrderSagaStates.AnyAsync(s => s.OrderId == orderId));
    }

    [Fact]
    public async Task Given_StockReservingSaga_When_StockReservedReplyArrives_Then_SagaAdvancesToStockReserved()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        var commandId = await OpenSaga(orderCreated);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var sagaId = await sagaContext.OrderSagaStates
            .Where(s => s.OrderId == orderId)
            .Select(s => s.SagaId)
            .SingleAsync();
        var stockReserved = new StockReservedEvent(
            orderId,
            [new ReservedItem(101, 1, 2)],
            25m,
            "USD")
        {
            CausationId = commandId,
            SagaId = sagaId
        };
        var handler = ActivatorUtilities.CreateInstance<StockReservedEventHandler>(scope.ServiceProvider);

        await handler.Handle(stockReserved);

        sagaContext.ChangeTracker.Clear();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(OrderSagaStep.StockReserved.ToString(), saga.CurrentStep);
        Assert.Equal(nameof(StockReservedEvent), saga.OrderSagaState!.LastStepResult);
        Assert.Equal(2, saga.Transitions.Count);
        Assert.Contains(saga.Transitions, t =>
            t.FromStep == OrderSagaStep.StockReserving.ToString()
            && t.ToStep == OrderSagaStep.StockReserved.ToString()
            && t.TriggerMessageId == stockReserved.Id);
    }

    private async Task Handle(OrderCreatedEvent orderCreated, SagaOrchestratorOptions options)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<OrderCreatedEventHandler>(
            scope.ServiceProvider,
            Options.Create(options));

        await handler.Handle(orderCreated);
    }

    private async Task AssertSagaOpened(OrderCreatedEvent orderCreated)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .Include(s => s.Transitions)
            .SingleAsync(s => s.OrderSagaState!.OrderId == orderCreated.OrderId);

        Assert.Equal("Order", saga.SagaType);
        Assert.Equal(OrderSagaStep.StockReserving.ToString(), saga.CurrentStep);
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Equal(orderCreated.CorrelationId, saga.CorrelationId);
        Assert.Equal(orderCreated.OrderId, saga.OrderSagaState!.OrderId);
        var transition = Assert.Single(saga.Transitions);
        Assert.Equal(OrderSagaStep.Started.ToString(), transition.FromStep);
        Assert.Equal(OrderSagaStep.StockReserving.ToString(), transition.ToStep);
        Assert.Equal(orderCreated.Id, transition.TriggerMessageId);

        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        var commandRow = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(ReserveStockCommand), StringComparison.Ordinal)
            && e.Data.Contains(orderCreated.OrderId.ToString(), StringComparison.Ordinal));
        using var document = JsonDocument.Parse(commandRow.Data);
        var root = document.RootElement;
        Assert.Equal(orderCreated.OrderId, root.GetProperty(nameof(ReserveStockCommand.OrderId)).GetGuid());
        Assert.Equal(orderCreated.Id, root.GetProperty(nameof(ReserveStockCommand.CausationId)).GetGuid());
        Assert.Equal(saga.SagaId, root.GetProperty(nameof(ReserveStockCommand.SagaId)).GetGuid());
    }

    private async Task<Guid> OpenSaga(OrderCreatedEvent orderCreated)
    {
        await Handle(orderCreated, new SagaOrchestratorOptions
        {
            Enabled = true,
            AllowList = [orderCreated.OrderId]
        });

        using var scope = _factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        return outboxEvents.Single(e =>
            e.EventType.Contains(nameof(ReserveStockCommand), StringComparison.Ordinal)
            && e.Data.Contains(orderCreated.OrderId.ToString(), StringComparison.Ordinal)).Id;
    }

    private static OrderCreatedEvent CreateOrderCreated(Guid orderId) =>
        new(orderId, "customer-1", [new OrderItem("101", 2, 12.50m)], "USD")
        {
            CorrelationId = Guid.NewGuid()
        };
}
