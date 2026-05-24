using System.Text.Json;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Infrastructure.Data.EntityFramework;
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
    public async Task Given_OrderCreated_When_HandlerRuns_Then_SagaOpensAndReserveCommandIsQueued()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        await Handle(orderCreated);

        await AssertSagaOpened(orderCreated);
    }

    [Fact]
    public async Task Given_StockReservingSaga_When_StockReservedReplyArrives_Then_SagaAdvancesToPaymentAuthorizing()
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
        Assert.Equal(OrderSagaStep.PaymentAuthorizing.ToString(), saga.CurrentStep);
        Assert.Equal(nameof(AuthorizePaymentCommand), saga.OrderSagaState!.LastStepResult);
        Assert.Equal(2, saga.Transitions.Count);
        Assert.Contains(saga.Transitions, t =>
            t.FromStep == OrderSagaStep.StockReserving.ToString()
            && t.ToStep == OrderSagaStep.PaymentAuthorizing.ToString()
            && t.TriggerMessageId == stockReserved.Id);
    }

    [Fact]
    public async Task Given_OpenSaga_When_HappyPathRepliesArrive_Then_SagaReachesCompleted()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        var reserveCommandId = await OpenSaga(orderCreated);
        var sagaId = await GetSagaIdAsync(orderId);

        var stockReserved = new StockReservedEvent(
            orderId, [new ReservedItem(101, 1, 2)], 25m, "USD")
        {
            CausationId = reserveCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<StockReservedEventHandler, StockReservedEvent>(stockReserved);

        var authorizeCommandId = await GetLatestCommandIdAsync(nameof(AuthorizePaymentCommand));
        var paymentAuthorized = new PaymentAuthorizedEvent(
            Guid.NewGuid(), orderId, "customer-1", 25m, "USD")
        {
            CausationId = authorizeCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<PaymentAuthorizedEventHandler, PaymentAuthorizedEvent>(paymentAuthorized);

        var confirmCommandId = await GetLatestCommandIdAsync(nameof(ConfirmOrderCommand));
        var orderConfirmed = new OrderConfirmedEvent(orderId, "customer-1")
        {
            CausationId = confirmCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<OrderConfirmedEventHandler, OrderConfirmedEvent>(orderConfirmed);

        var commitCommandId = await GetLatestCommandIdAsync(nameof(CommitStockCommand));
        var stockCommitted = new StockCommittedEvent(orderId, [new CommittedItem(101, 1, 2)])
        {
            CausationId = commitCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<StockCommittedEventHandler, StockCommittedEvent>(stockCommitted);

        var createShipmentCommandId = await GetLatestCommandIdAsync(nameof(CreateShipmentCommand));
        var shipmentCreated = new ShipmentCreatedEvent(
            Guid.NewGuid(), orderId, "customer-1", 1, [new ShipmentLineItem(101, 2)])
        {
            CausationId = createShipmentCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<ShipmentCreatedEventHandler, ShipmentCreatedEvent>(shipmentCreated);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        sagaContext.ChangeTracker.Clear();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(SagaStatus.Completed, saga.Status);
        Assert.Equal(OrderSagaStep.Completed.ToString(), saga.CurrentStep);
        Assert.Equal(6, saga.Transitions.Count);
    }

    [Fact]
    public async Task Given_OpenSaga_When_PaymentFailedArrives_Then_SagaCompensatesAndEmitsReleaseStockCommand()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        var reserveCommandId = await OpenSaga(orderCreated);
        var sagaId = await GetSagaIdAsync(orderId);

        var stockReserved = new StockReservedEvent(
            orderId, [new ReservedItem(101, 1, 2)], 25m, "USD")
        {
            CausationId = reserveCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<StockReservedEventHandler, StockReservedEvent>(stockReserved);

        var authorizeCommandId = await GetLatestCommandIdAsync(nameof(AuthorizePaymentCommand));
        var paymentFailed = new PaymentFailedEvent(
            Guid.NewGuid(), orderId, "customer-1", "Declined")
        {
            CausationId = authorizeCommandId,
            SagaId = sagaId,
        };

        await DispatchAsync<PaymentFailedEventHandler, PaymentFailedEvent>(paymentFailed);

        using (var scope = _factory.Services.CreateScope())
        {
            var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
            sagaContext.ChangeTracker.Clear();
            var saga = await sagaContext.SagaInstances
                .Include(s => s.OrderSagaState)
                .Include(s => s.Transitions)
                .SingleAsync(s => s.SagaId == sagaId);

            Assert.Equal(SagaStatus.Compensating, saga.Status);
            Assert.Equal(OrderSagaStep.ReleasingStock.ToString(), saga.CurrentStep);
            Assert.Equal(OrderSagaStep.StockReserved.ToString(), saga.OrderSagaState!.CompensationOrigin);
            Assert.Contains(saga.Transitions, t =>
                t.FromStep == OrderSagaStep.PaymentAuthorizing.ToString()
                && t.ToStep == OrderSagaStep.ReleasingStock.ToString()
                && t.Error != null && t.Error.Contains("Payment failed", StringComparison.Ordinal));

            var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
            Assert.Contains(unpublished, e =>
                e.EventType.Contains(nameof(ReleaseStockCommand), StringComparison.Ordinal)
                && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        var releaseCommandId = await GetLatestCommandIdAsync(nameof(ReleaseStockCommand));
        var stockReleased = new StockReleasedEvent(orderId, [])
        {
            CausationId = releaseCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<StockReleasedEventHandler, StockReleasedEvent>(stockReleased);

        using (var scope = _factory.Services.CreateScope())
        {
            var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
            sagaContext.ChangeTracker.Clear();
            var saga = await sagaContext.SagaInstances
                .SingleAsync(s => s.SagaId == sagaId);
            Assert.Equal(SagaStatus.Compensating, saga.Status);
            Assert.Equal(OrderSagaStep.CancellingOrder.ToString(), saga.CurrentStep);
        }

        var cancelCommandId = await GetLatestCommandIdAsync(nameof(CancelOrderCommand));
        var orderCancelled = new OrderCancelledEvent(orderId, "customer-1")
        {
            CausationId = cancelCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<OrderCancelledEventHandler, OrderCancelledEvent>(orderCancelled);

        using (var scope = _factory.Services.CreateScope())
        {
            var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
            sagaContext.ChangeTracker.Clear();
            var saga = await sagaContext.SagaInstances
                .SingleAsync(s => s.SagaId == sagaId);
            Assert.Equal(SagaStatus.Compensated, saga.Status);
            Assert.Equal(OrderSagaStep.Compensated.ToString(), saga.CurrentStep);
        }
    }

    private async Task Handle(OrderCreatedEvent orderCreated)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<OrderCreatedEventHandler>(scope.ServiceProvider);

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
        await Handle(orderCreated);

        using var scope = _factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        return outboxEvents.Single(e =>
            e.EventType.Contains(nameof(ReserveStockCommand), StringComparison.Ordinal)
            && e.Data.Contains(orderCreated.OrderId.ToString(), StringComparison.Ordinal)).Id;
    }

    private async Task<Guid> GetSagaIdAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        return await sagaContext.OrderSagaStates
            .Where(s => s.OrderId == orderId)
            .Select(s => s.SagaId)
            .SingleAsync();
    }

    private async Task<Guid> GetLatestCommandIdAsync(string commandTypeName)
    {
        using var scope = _factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        return outboxEvents.Last(e =>
            e.EventType.Contains(commandTypeName, StringComparison.Ordinal)).Id;
    }

    private async Task DispatchAsync<THandler, TEvent>(TEvent @event)
        where THandler : IEventHandler<TEvent>
        where TEvent : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = _factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        await handler.Handle(@event);
    }

    private static OrderCreatedEvent CreateOrderCreated(Guid orderId) =>
        new(orderId, "customer-1", [new OrderItem("101", 2, 12.50m)], "USD")
        {
            CorrelationId = Guid.NewGuid()
        };
}
