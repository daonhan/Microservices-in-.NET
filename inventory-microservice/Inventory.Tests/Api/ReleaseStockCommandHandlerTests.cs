using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Inventory.Service.IntegrationEvents;
using Inventory.Service.IntegrationEvents.EventHandlers;
using Inventory.Service.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Api;

public class ReleaseStockCommandHandlerTests : IntegrationTestBase
{
    public ReleaseStockCommandHandlerTests(InventoryWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_HeldReservation_When_ReleaseStockCommandHandled_Then_ReleasesAndEmitsStockReleasedReply()
    {
        const int productId = 701;
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 3,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 3,
        });
        InventoryContext.StockReservations.Add(new StockReservation
        {
            OrderId = orderId,
            ProductId = productId,
            WarehouseId = 1,
            Quantity = 3,
            Status = ReservationStatus.Held,
            CreatedAt = DateTime.UtcNow,
        });
        await InventoryContext.SaveChangesAsync();

        var command = new ReleaseStockCommand(orderId, Guid.NewGuid(), sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<ReleaseStockCommandHandler>(scope.ServiceProvider);
            await handler.Handle(command);
        }

        InventoryContext.ChangeTracker.Clear();

        var reservation = InventoryContext.StockReservations.Single(r => r.OrderId == orderId);
        Assert.Equal(ReservationStatus.Released, reservation.Status);

        var item = InventoryContext.StockItems.Single(s => s.ProductId == productId);
        Assert.Equal(10, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);

        await AssertReleasedReplyAsync(orderId, command, productId, quantity: 3);
    }

    [Fact]
    public async Task Given_AlreadyReleased_When_ReleaseStockCommandReplayed_Then_EmitsReplyIdempotently()
    {
        const int productId = 702;
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        InventoryContext.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 10,
            TotalReserved = 2,
            LowStockThreshold = 0,
        });
        InventoryContext.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = 1,
            OnHand = 10,
            Reserved = 2,
        });
        InventoryContext.StockReservations.Add(new StockReservation
        {
            OrderId = orderId,
            ProductId = productId,
            WarehouseId = 1,
            Quantity = 2,
            Status = ReservationStatus.Held,
            CreatedAt = DateTime.UtcNow,
        });
        await InventoryContext.SaveChangesAsync();

        var first = new ReleaseStockCommand(orderId, Guid.NewGuid(), sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<ReleaseStockCommandHandler>(scope.ServiceProvider);
            await handler.Handle(first);
        }

        await ClearOutboxAsync();

        var replay = new ReleaseStockCommand(orderId, Guid.NewGuid(), sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<ReleaseStockCommandHandler>(scope.ServiceProvider);
            await handler.Handle(replay);
        }

        await AssertReleasedReplyAsync(orderId, replay, productId, quantity: 0, expectAtLeastOneItem: false);
    }

    private async Task AssertReleasedReplyAsync(
        Guid orderId,
        ReleaseStockCommand command,
        int productId,
        int quantity,
        bool expectAtLeastOneItem = true)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(StockReleasedEvent), StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(orderId, root.GetProperty(nameof(StockReleasedEvent.OrderId)).GetGuid());
        Assert.Equal(command.Id, root.GetProperty("CausationId").GetGuid());
        Assert.Equal(command.SagaId, root.GetProperty("SagaId").GetGuid());
        Assert.Equal(command.CorrelationId, root.GetProperty("CorrelationId").GetGuid());

        var items = root.GetProperty(nameof(StockReleasedEvent.Items));
        if (expectAtLeastOneItem)
        {
            Assert.Contains(items.EnumerateArray(),
                item => item.GetProperty(nameof(ReleasedItem.ProductId)).GetInt32() == productId
                    && item.GetProperty(nameof(ReleasedItem.Quantity)).GetInt32() == quantity);
        }
    }

    private async Task ClearOutboxAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unpublished = await outboxStore.GetUnpublishedOutboxEvents();
        foreach (var entry in unpublished)
        {
            await outboxStore.MarkOutboxEventAsPublished(entry.Id);
        }
    }
}
