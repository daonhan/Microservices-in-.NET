using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Features.OrderSaga.OrderCreated;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Reaper;

namespace Saga.Tests.Domain;

public class OrderCreatedEventHandlerTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public OrderCreatedEventHandlerTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_OrderCreated_When_HandlerRuns_Then_SagaStartsForEveryOrder()
    {
        var orderCreated = CreateOrderCreated();

        await HandleOrderCreated(orderCreated);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        Assert.True(await sagaContext.OrderSagaStates.AnyAsync(s => s.OrderId == orderCreated.OrderId));
    }

    [Fact]
    public async Task Given_SameOrderIdSeenTwice_When_OrderCreatedHandlerRuns_Then_OnlyOneSagaAndReserveCommandAreStored()
    {
        var orderCreated = CreateOrderCreated();
        var duplicate = orderCreated with
        {
            Id = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid()
        };

        await HandleOrderCreated(orderCreated);
        await HandleOrderCreated(duplicate);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .Include(s => s.Transitions)
            .SingleAsync(s => s.OrderSagaState!.OrderId == orderCreated.OrderId);

        Assert.Equal(OrderSagaStep.StockReserving.ToString(), saga.CurrentStep);
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Single(saga.Transitions);

        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var reserveCommands = (await outboxStore.GetUnpublishedOutboxEvents())
            .Where(e => e.EventType.Contains(nameof(ReserveStockCommand), StringComparison.Ordinal))
            .Select(e => JsonSerializer.Deserialize<ReserveStockCommand>(e.Data))
            .Where(c => c?.OrderId == orderCreated.OrderId)
            .ToArray();

        var reserveCommand = Assert.Single(reserveCommands);
        Assert.Equal(orderCreated.Id, reserveCommand!.CausationId);
        Assert.Equal(saga.SagaId, reserveCommand.SagaId);
    }

    private async Task HandleOrderCreated(OrderCreatedEvent orderCreated)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = new OrderCreatedHandler(
            scope.ServiceProvider.GetRequiredService<SagaContext>(),
            scope.ServiceProvider.GetRequiredService<IOutboxUnitOfWork>(),
            TimeProvider.System,
            new OrderSagaTimeoutScheduler(Options.Create(new OrderSagaTimeoutOptions())),
            NullLogger<OrderCreatedHandler>.Instance);

        await handler.Handle(orderCreated);
    }

    private static OrderCreatedEvent CreateOrderCreated() =>
        new(Guid.NewGuid(), "customer-1", [new OrderItem("101", 2, 12.50m)], "USD")
        {
            CorrelationId = Guid.NewGuid()
        };
}
