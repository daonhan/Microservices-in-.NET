using System.Globalization;
using ECommerce.Shared.IntegrationEvents.Commands;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.IntegrationEvents.EventHandlers;
using Inventory.Service.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Api;

public class ReserveStockCommandHandlerTests : IntegrationTestBase
{
    public ReserveStockCommandHandlerTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Given_ReserveStockCommand_When_Handled_Then_ReservesStockAndPublishesStockReservedReply()
    {
        const int productId = 401;
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var command = new ReserveStockCommand(
            orderId,
            "customer-1",
            [new ReserveStockItem(productId.ToString(CultureInfo.InvariantCulture), 3, 10m)],
            "USD",
            Guid.NewGuid(),
            sagaId)
        {
            CorrelationId = Guid.NewGuid()
        };

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        Subscribe<StockReservedEvent>();

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<ReserveStockCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        InventoryContext.ChangeTracker.Clear();
        var reservation = InventoryContext.StockReservations.Single(r => r.OrderId == orderId);
        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Equal(3, reservation.Quantity);

        SpinWait.SpinUntil(() => ReceivedEvents.Count > 0, TimeSpan.FromSeconds(5));
        var published = Assert.IsType<StockReservedEvent>(ReceivedEvents.First());
        Assert.Equal(orderId, published.OrderId);
        Assert.Equal(command.Id, published.CausationId);
        Assert.Equal(sagaId, published.SagaId);
        Assert.Equal(command.CorrelationId, published.CorrelationId);
    }

    [Fact]
    public async Task Given_ReservationAlreadyHeld_When_ReserveStockCommandReplayed_Then_PublishesStockReservedReply()
    {
        const int productId = 402;
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var command = new ReserveStockCommand(
            orderId,
            "customer-1",
            [new ReserveStockItem(productId.ToString(CultureInfo.InvariantCulture), 2, 10m)],
            "USD",
            Guid.NewGuid(),
            sagaId)
        {
            CorrelationId = Guid.NewGuid()
        };

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 0,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 0,
        });
        await InventoryContext.SaveChangesAsync();

        Subscribe<StockReservedEvent>();

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<ReserveStockCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);
        SpinWait.SpinUntil(() => ReceivedEvents.Count > 0, TimeSpan.FromSeconds(5));
        ReceivedEvents.Clear();

        await handler.Handle(command);

        SpinWait.SpinUntil(() => ReceivedEvents.Count > 0, TimeSpan.FromSeconds(5));
        var published = Assert.IsType<StockReservedEvent>(ReceivedEvents.First());
        Assert.Equal(orderId, published.OrderId);
        Assert.Equal(command.Id, published.CausationId);
        Assert.Equal(sagaId, published.SagaId);
        Assert.Equal(2, published.Items.Single().Quantity);
    }
}
