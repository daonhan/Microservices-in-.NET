using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Features.OrderSaga.OrderCancelled;
using Saga.Service.Features.OrderSaga.OrderCreated;
using Saga.Service.Features.OrderSaga.PaymentFailed;
using Saga.Service.Features.OrderSaga.StockReleased;
using Saga.Service.Features.OrderSaga.StockReserved;
using Saga.Service.Infrastructure.Data.EntityFramework;

namespace Saga.Tests.Features.OrderSaga.PaymentFailed;

public class EndpointTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public EndpointTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
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
        await DispatchAsync<StockReservedHandler, StockReservedEvent>(stockReserved);

        var authorizeCommandId = await GetLatestCommandIdAsync(nameof(AuthorizePaymentCommand));
        var paymentFailed = new PaymentFailedEvent(
            Guid.NewGuid(), orderId, "customer-1", "Declined")
        {
            CausationId = authorizeCommandId,
            SagaId = sagaId,
        };

        await DispatchAsync<PaymentFailedHandler, PaymentFailedEvent>(paymentFailed);

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
        await DispatchAsync<StockReleasedHandler, StockReleasedEvent>(stockReleased);

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
        await DispatchAsync<OrderCancelledHandler, OrderCancelledEvent>(orderCancelled);

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
        var handler = ActivatorUtilities.CreateInstance<OrderCreatedHandler>(scope.ServiceProvider);

        await handler.Handle(orderCreated);
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
