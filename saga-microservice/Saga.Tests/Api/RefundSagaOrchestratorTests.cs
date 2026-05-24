using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.RefundSaga;
using Saga.Service.Features.OrderSaga.OrderCancelled;
using Saga.Service.Features.OrderSaga.ShipmentCancelled;
using Saga.Service.Features.OrderSaga.ShipmentFailed;
using Saga.Service.Features.RefundSaga.RefundRequested;
using Saga.Service.Infrastructure.Data.EntityFramework;
using RefundSagaPaymentRefundedHandler = Saga.Service.Features.RefundSaga.PaymentRefunded.PaymentRefundedHandler;

namespace Saga.Tests.Api;

public class RefundSagaOrchestratorTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public RefundSagaOrchestratorTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_RefundRequested_When_HandlerRuns_Then_SagaOpensAndRefundCommandQueued()
    {
        var orderId = Guid.NewGuid();
        var requested = CreateRefundRequested(orderId, shipmentId: Guid.NewGuid());

        await OpenSaga(requested);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.RefundSagaState)
            .Include(s => s.Transitions)
            .SingleAsync(s => s.RefundSagaState!.OrderId == orderId);

        Assert.Equal("Refund", saga.SagaType);
        Assert.Equal(RefundSagaStep.PaymentRefunding.ToString(), saga.CurrentStep);
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Equal(requested.PaymentId, saga.RefundSagaState!.PaymentId);
        var transition = Assert.Single(saga.Transitions);
        Assert.Equal(RefundSagaStep.Started.ToString(), transition.FromStep);
        Assert.Equal(RefundSagaStep.PaymentRefunding.ToString(), transition.ToStep);

        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(outboxEvents, e =>
            e.EventType.Contains(nameof(RefundPaymentCommand), StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Given_OpenSaga_When_RefundedThenShipmentCancelled_Then_SagaCompletes()
    {
        var orderId = Guid.NewGuid();
        var requested = CreateRefundRequested(orderId, shipmentId: Guid.NewGuid());

        var refundCommandId = await OpenSaga(requested);
        var sagaId = await GetSagaIdAsync(orderId);

        var refunded = new PaymentRefundedEvent(requested.PaymentId, orderId, requested.RefundAmount)
        {
            CausationId = refundCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<RefundSagaPaymentRefundedHandler, PaymentRefundedEvent>(refunded);

        var cancelShipmentCommandId = await GetLatestCommandIdAsync(nameof(CancelShipmentCommand));
        var shipmentCancelled = new ShipmentCancelledEvent(
            Guid.NewGuid(), orderId, "customer-1", DateTime.UtcNow, "Refund")
        {
            CausationId = cancelShipmentCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<ShipmentCancelledHandler, ShipmentCancelledEvent>(shipmentCancelled);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        sagaContext.ChangeTracker.Clear();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.Equal(SagaStatus.Completed, saga.Status);
        Assert.Equal(RefundSagaStep.Completed.ToString(), saga.CurrentStep);
        Assert.Equal(3, saga.Transitions.Count);
    }

    [Fact]
    public async Task Given_OpenSaga_When_ShipmentFailsAfterRefund_Then_SagaCompensatesAndCancelsOrder()
    {
        var orderId = Guid.NewGuid();
        var requested = CreateRefundRequested(orderId, shipmentId: Guid.NewGuid());

        var refundCommandId = await OpenSaga(requested);
        var sagaId = await GetSagaIdAsync(orderId);

        var refunded = new PaymentRefundedEvent(requested.PaymentId, orderId, requested.RefundAmount)
        {
            CausationId = refundCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<RefundSagaPaymentRefundedHandler, PaymentRefundedEvent>(refunded);

        var cancelShipmentCommandId = await GetLatestCommandIdAsync(nameof(CancelShipmentCommand));
        var shipmentFailed = new ShipmentFailedEvent(
            Guid.NewGuid(), orderId, "customer-1", null, null, "Carrier down", DateTime.UtcNow)
        {
            CausationId = cancelShipmentCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<ShipmentFailedHandler, ShipmentFailedEvent>(shipmentFailed);

        using (var scope = _factory.Services.CreateScope())
        {
            var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
            sagaContext.ChangeTracker.Clear();
            var saga = await sagaContext.SagaInstances
                .SingleAsync(s => s.SagaId == sagaId);
            Assert.Equal(SagaStatus.Compensating, saga.Status);
            Assert.Equal(RefundSagaStep.CancellingOrder.ToString(), saga.CurrentStep);

            var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
            Assert.Contains(unpublished, e =>
                e.EventType.Contains(nameof(CancelOrderCommand), StringComparison.Ordinal)
                && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        var cancelOrderCommandId = await GetLatestCommandIdAsync(nameof(CancelOrderCommand));
        var orderCancelled = new OrderCancelledEvent(orderId, "customer-1")
        {
            CausationId = cancelOrderCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<OrderCancelledHandler, OrderCancelledEvent>(orderCancelled);

        using (var scope = _factory.Services.CreateScope())
        {
            var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
            sagaContext.ChangeTracker.Clear();
            var saga = await sagaContext.SagaInstances.SingleAsync(s => s.SagaId == sagaId);
            Assert.Equal(SagaStatus.Compensated, saga.Status);
            Assert.Equal(RefundSagaStep.Compensated.ToString(), saga.CurrentStep);
        }
    }

    private async Task Handle(RefundRequestedEvent requested)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<RefundRequestedHandler>(scope.ServiceProvider);

        await handler.Handle(requested);
    }

    private async Task<Guid> OpenSaga(RefundRequestedEvent requested)
    {
        await Handle(requested);

        using var scope = _factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        return outboxEvents.Single(e =>
            e.EventType.Contains(nameof(RefundPaymentCommand), StringComparison.Ordinal)
            && e.Data.Contains(requested.OrderId.ToString(), StringComparison.Ordinal)).Id;
    }

    private async Task<Guid> GetSagaIdAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        return await sagaContext.RefundSagaStates
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

    private static RefundRequestedEvent CreateRefundRequested(Guid orderId, Guid? shipmentId) =>
        new(orderId, Guid.NewGuid(), shipmentId, 42.50m, "USD")
        {
            CorrelationId = Guid.NewGuid()
        };
}
