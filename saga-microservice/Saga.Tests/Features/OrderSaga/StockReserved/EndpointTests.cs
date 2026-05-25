using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Features.OrderSaga.OrderCreated;
using Saga.Service.Features.OrderSaga.StockReserved;
using Saga.Service.Infrastructure.Data.EntityFramework;

namespace Saga.Tests.Features.OrderSaga.StockReserved;

public class EndpointTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public EndpointTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
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
        var handler = ActivatorUtilities.CreateInstance<StockReservedHandler>(scope.ServiceProvider);

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

    private static OrderCreatedEvent CreateOrderCreated(Guid orderId) =>
        new(orderId, "customer-1", [new OrderItem("101", 2, 12.50m)], "USD")
        {
            CorrelationId = Guid.NewGuid()
        };
}
