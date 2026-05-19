using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.IntegrationEvents;
using Saga.Service.IntegrationEvents.EventHandlers;
using Saga.Service.Models;

namespace Saga.Tests.EndToEnd;

[Trait("Category", "EndToEnd")]
public sealed class OrderSagaEndToEndTests : IClassFixture<SagaEndToEndFixture>
{
    private readonly SagaEndToEndFixture _fixture;
    private SagaEndToEndWebApplicationFactory Factory => _fixture.Factory;

    public OrderSagaEndToEndTests(SagaEndToEndFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_OrchestratedOrder_ReachesCompleted()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = NewOrderCreated(orderId);

        var reserveId = await OpenSaga(orderCreated);
        var sagaId = await GetSagaIdAsync(orderId);

        await Dispatch<StockReservedEventHandler, StockReservedEvent>(new StockReservedEvent(
            orderId, [new ReservedItem(101, 1, 2)], 25m, "USD")
        {
            CausationId = reserveId,
            SagaId = sagaId,
        });

        var authorizeId = await GetLatestCommandId(nameof(AuthorizePaymentCommand));
        await Dispatch<PaymentAuthorizedEventHandler, PaymentAuthorizedEvent>(new PaymentAuthorizedEvent(
            Guid.NewGuid(), orderId, "customer-1", 25m, "USD")
        {
            CausationId = authorizeId,
            SagaId = sagaId,
        });

        var confirmId = await GetLatestCommandId(nameof(ConfirmOrderCommand));
        await Dispatch<OrderConfirmedEventHandler, OrderConfirmedEvent>(new OrderConfirmedEvent(orderId, "customer-1")
        {
            CausationId = confirmId,
            SagaId = sagaId,
        });

        var commitId = await GetLatestCommandId(nameof(CommitStockCommand));
        await Dispatch<StockCommittedEventHandler, StockCommittedEvent>(new StockCommittedEvent(
            orderId, [new CommittedItem(101, 1, 2)])
        {
            CausationId = commitId,
            SagaId = sagaId,
        });

        var shipmentId = await GetLatestCommandId(nameof(CreateShipmentCommand));
        await Dispatch<ShipmentCreatedEventHandler, ShipmentCreatedEvent>(new ShipmentCreatedEvent(
            Guid.NewGuid(), orderId, "customer-1", 1, [new ShipmentLineItem(101, 2)])
        {
            CausationId = shipmentId,
            SagaId = sagaId,
        });

        using var scope = Factory.Services.CreateScope();
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
    public async Task FailureBranch_PaymentDeclines_SagaCompensatesToCompensated()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = NewOrderCreated(orderId);

        var reserveId = await OpenSaga(orderCreated);
        var sagaId = await GetSagaIdAsync(orderId);

        await Dispatch<StockReservedEventHandler, StockReservedEvent>(new StockReservedEvent(
            orderId, [new ReservedItem(101, 1, 2)], 25m, "USD")
        {
            CausationId = reserveId,
            SagaId = sagaId,
        });

        var authorizeId = await GetLatestCommandId(nameof(AuthorizePaymentCommand));
        await Dispatch<PaymentFailedEventHandler, PaymentFailedEvent>(new PaymentFailedEvent(
            Guid.NewGuid(), orderId, "customer-1", "Declined")
        {
            CausationId = authorizeId,
            SagaId = sagaId,
        });

        using (var scope = Factory.Services.CreateScope())
        {
            var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
            sagaContext.ChangeTracker.Clear();
            var compensating = await sagaContext.SagaInstances
                .SingleAsync(s => s.SagaId == sagaId);
            Assert.Equal(SagaStatus.Compensating, compensating.Status);
            Assert.Equal(OrderSagaStep.ReleasingStock.ToString(), compensating.CurrentStep);
        }

        var releaseId = await GetLatestCommandId(nameof(ReleaseStockCommand));
        await Dispatch<StockReleasedEventHandler, StockReleasedEvent>(new StockReleasedEvent(orderId, [])
        {
            CausationId = releaseId,
            SagaId = sagaId,
        });

        using (var scope = Factory.Services.CreateScope())
        {
            var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
            sagaContext.ChangeTracker.Clear();
            var cancelling = await sagaContext.SagaInstances
                .SingleAsync(s => s.SagaId == sagaId);
            Assert.Equal(SagaStatus.Compensating, cancelling.Status);
            Assert.Equal(OrderSagaStep.CancellingOrder.ToString(), cancelling.CurrentStep);
        }

        var cancelId = await GetLatestCommandId(nameof(CancelOrderCommand));
        await Dispatch<OrderCancelledEventHandler, OrderCancelledEvent>(new OrderCancelledEvent(orderId, "customer-1")
        {
            CausationId = cancelId,
            SagaId = sagaId,
        });

        using (var scope = Factory.Services.CreateScope())
        {
            var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
            sagaContext.ChangeTracker.Clear();
            var compensated = await sagaContext.SagaInstances
                .SingleAsync(s => s.SagaId == sagaId);
            Assert.Equal(SagaStatus.Compensated, compensated.Status);
            Assert.Equal(OrderSagaStep.Compensated.ToString(), compensated.CurrentStep);
        }
    }

    private static OrderCreatedEvent NewOrderCreated(Guid orderId) =>
        new(orderId, "customer-1", [new OrderItem("101", 2, 12.50m)], "USD")
        {
            CorrelationId = Guid.NewGuid(),
        };

    private async Task<Guid> OpenSaga(OrderCreatedEvent orderCreated)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<OrderCreatedEventHandler>(scope.ServiceProvider);

        await handler.Handle(orderCreated);

        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outbox = await outboxStore.GetUnpublishedOutboxEvents();
        return outbox.Single(e =>
            e.EventType.Contains(nameof(ReserveStockCommand), StringComparison.Ordinal)
            && e.Data.Contains(orderCreated.OrderId.ToString(), StringComparison.Ordinal)).Id;
    }

    private async Task<Guid> GetSagaIdAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        return await sagaContext.OrderSagaStates
            .Where(s => s.OrderId == orderId)
            .Select(s => s.SagaId)
            .SingleAsync();
    }

    private async Task<Guid> GetLatestCommandId(string commandTypeName)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outbox = await outboxStore.GetUnpublishedOutboxEvents();
        return outbox.Last(e => e.EventType.Contains(commandTypeName, StringComparison.Ordinal)).Id;
    }

    private async Task Dispatch<THandler, TEvent>(TEvent @event)
        where THandler : IEventHandler<TEvent>
        where TEvent : Event
    {
        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        await handler.Handle(@event);
    }
}
