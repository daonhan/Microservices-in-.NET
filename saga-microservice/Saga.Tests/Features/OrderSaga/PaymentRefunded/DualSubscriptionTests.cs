using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Domain.RefundSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;
using OrderSagaOrderCancelledHandler = Saga.Service.Features.OrderSaga.OrderCancelled.OrderCancelledHandler;
using OrderSagaPaymentFailedHandler = Saga.Service.Features.OrderSaga.PaymentFailed.PaymentFailedHandler;
using OrderSagaPaymentRefundedHandler = Saga.Service.Features.OrderSaga.PaymentRefunded.PaymentRefundedHandler;
using OrderSagaShipmentCancelledHandler = Saga.Service.Features.OrderSaga.ShipmentCancelled.ShipmentCancelledHandler;
using OrderSagaShipmentFailedHandler = Saga.Service.Features.OrderSaga.ShipmentFailed.ShipmentFailedHandler;
using RefundSagaOrderCancelledHandler = Saga.Service.Features.RefundSaga.OrderCancelled.OrderCancelledHandler;
using RefundSagaPaymentFailedHandler = Saga.Service.Features.RefundSaga.PaymentFailed.PaymentFailedHandler;
using RefundSagaPaymentRefundedHandler = Saga.Service.Features.RefundSaga.PaymentRefunded.PaymentRefundedHandler;
using RefundSagaShipmentCancelledHandler = Saga.Service.Features.RefundSaga.ShipmentCancelled.ShipmentCancelledHandler;
using RefundSagaShipmentFailedHandler = Saga.Service.Features.RefundSaga.ShipmentFailed.ShipmentFailedHandler;

namespace Saga.Tests.Features.OrderSaga.PaymentRefunded;

public class DualSubscriptionTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public DualSubscriptionTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [MemberData(nameof(SharedRefundReplyRegistrations))]
    public void Given_SharedRefundReplyEvent_Then_OrderAndRefundHandlersRegistered(
        Type eventType,
        Type orderHandlerType,
        Type refundHandlerType)
    {
        using var scope = _factory.Services.CreateScope();
        var handlers = scope.ServiceProvider
            .GetKeyedServices<IEventHandler>(eventType)
            .ToList();

        Assert.Equal(2, handlers.Count);
        Assert.Contains(handlers, h => h.GetType() == orderHandlerType);
        Assert.Contains(handlers, h => h.GetType() == refundHandlerType);
    }

    [Fact]
    public async Task Given_OrderSagaOnly_When_BothHandlersInvoked_Then_OrderSagaAdvancesAndRefundHandlerNoOps()
    {
        var orderId = Guid.NewGuid();
        var orderSagaId = await SeedOrderSagaInRefundingPayment(orderId, amount: 25m);

        var refunded = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 25m)
        {
            CausationId = Guid.NewGuid(),
            SagaId = orderSagaId,
        };

        await InvokeBothHandlers(refunded);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var orderSaga = await sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == orderSagaId);
        Assert.Equal(OrderSagaStep.CancellingOrder.ToString(), orderSaga.CurrentStep);
        Assert.NotEmpty(orderSaga.Transitions);

        Assert.False(await sagaContext.RefundSagaStates.AnyAsync(s => s.SagaId == orderSagaId));
    }

    [Fact]
    public async Task Given_RefundSagaOnly_When_BothHandlersInvoked_Then_RefundSagaAdvancesAndOrderHandlerNoOps()
    {
        var orderId = Guid.NewGuid();
        var refundSagaId = await SeedRefundSagaInPaymentRefunding(orderId, shipmentId: null);

        var refunded = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 42.50m)
        {
            CausationId = Guid.NewGuid(),
            SagaId = refundSagaId,
        };

        await InvokeBothHandlers(refunded);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var refundSaga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == refundSagaId);
        Assert.Equal(SagaStatus.Completed, refundSaga.Status);
        Assert.Equal(RefundSagaStep.Completed.ToString(), refundSaga.CurrentStep);

        Assert.False(await sagaContext.OrderSagaStates.AnyAsync(s => s.SagaId == refundSagaId));
    }

    [Fact]
    public async Task Given_BothSagasRunningConcurrently_When_EventTargetsOrderSaga_Then_OnlyOrderSagaAdvances()
    {
        var orderId = Guid.NewGuid();
        var refundOrderId = Guid.NewGuid();
        var orderSagaId = await SeedOrderSagaInRefundingPayment(orderId, amount: 25m);
        var refundSagaId = await SeedRefundSagaInPaymentRefunding(refundOrderId, shipmentId: null);

        var refunded = new PaymentRefundedEvent(Guid.NewGuid(), orderId, 25m)
        {
            CausationId = Guid.NewGuid(),
            SagaId = orderSagaId,
        };

        await InvokeBothHandlers(refunded);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var orderSaga = await sagaContext.SagaInstances
            .SingleAsync(s => s.SagaId == orderSagaId);
        Assert.Equal(OrderSagaStep.CancellingOrder.ToString(), orderSaga.CurrentStep);

        var refundSaga = await sagaContext.SagaInstances
            .SingleAsync(s => s.SagaId == refundSagaId);
        Assert.Equal(RefundSagaStep.PaymentRefunding.ToString(), refundSaga.CurrentStep);
        Assert.Equal(SagaStatus.Running, refundSaga.Status);
    }

    private async Task InvokeBothHandlers(PaymentRefundedEvent @event)
    {
        using var scope = _factory.Services.CreateScope();
        var handlers = scope.ServiceProvider
            .GetKeyedServices<IEventHandler>(typeof(PaymentRefundedEvent))
            .ToList();

        foreach (var handler in handlers)
        {
            await handler.Handle(@event);
        }
    }

    public static TheoryData<Type, Type, Type> SharedRefundReplyRegistrations() => new()
    {
        { typeof(PaymentFailedEvent), typeof(OrderSagaPaymentFailedHandler), typeof(RefundSagaPaymentFailedHandler) },
        { typeof(ShipmentFailedEvent), typeof(OrderSagaShipmentFailedHandler), typeof(RefundSagaShipmentFailedHandler) },
        { typeof(ShipmentCancelledEvent), typeof(OrderSagaShipmentCancelledHandler), typeof(RefundSagaShipmentCancelledHandler) },
        { typeof(OrderCancelledEvent), typeof(OrderSagaOrderCancelledHandler), typeof(RefundSagaOrderCancelledHandler) },
        { typeof(PaymentRefundedEvent), typeof(OrderSagaPaymentRefundedHandler), typeof(RefundSagaPaymentRefundedHandler) }
    };

    private async Task<Guid> SeedOrderSagaInRefundingPayment(Guid orderId, decimal amount)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var sagaId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var saga = new SagaInstance
        {
            SagaId = sagaId,
            SagaType = "Order",
            CurrentStep = OrderSagaStep.RefundingPayment.ToString(),
            Status = SagaStatus.Compensating,
            CorrelationId = Guid.NewGuid(),
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-1),
            OrderSagaState = new OrderSagaState
            {
                SagaId = sagaId,
                OrderId = orderId,
                Amount = amount,
                CompensationOrigin = OrderSagaStep.StockCommitted.ToString()
            }
        };
        sagaContext.SagaInstances.Add(saga);
        await sagaContext.SaveChangesAsync();
        return sagaId;
    }

    private async Task<Guid> SeedRefundSagaInPaymentRefunding(Guid orderId, Guid? shipmentId)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var sagaId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var saga = new SagaInstance
        {
            SagaId = sagaId,
            SagaType = "Refund",
            CurrentStep = RefundSagaStep.PaymentRefunding.ToString(),
            Status = SagaStatus.Running,
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
