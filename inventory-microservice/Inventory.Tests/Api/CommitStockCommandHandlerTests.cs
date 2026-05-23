using System.Globalization;
using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Domain;
using Inventory.Service.Features.CommitStock;
using Inventory.Service.Features.ReserveStock;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Api;

public class CommitStockCommandHandlerTests : IntegrationTestBase
{
    public CommitStockCommandHandlerTests(InventoryWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_HeldReservation_When_CommitStockCommandHandled_Then_CommitsAndEmitsStockCommittedReply()
    {
        const int productId = 601;
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        await SeedHeldReservationAsync(productId, orderId, quantity: 3);

        var command = new CommitStockCommand(
            orderId,
            causationId: Guid.NewGuid(),
            sagaId: sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<CommitStockCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        InventoryContext.ChangeTracker.Clear();
        var reservation = InventoryContext.StockReservations.Single(r => r.OrderId == orderId);
        Assert.Equal(ReservationStatus.Committed, reservation.Status);

        await AssertCommittedReplyAsync(orderId, command, productId);
    }

    [Fact]
    public async Task Given_AlreadyCommittedReservation_When_CommitCommandReplayed_Then_EmitsCommittedReplyIdempotently()
    {
        const int productId = 602;
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        await SeedHeldReservationAsync(productId, orderId, quantity: 1);

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<CommitStockCommandHandler>(scope.ServiceProvider);
            var first = new CommitStockCommand(orderId, Guid.NewGuid(), sagaId)
            {
                CorrelationId = Guid.NewGuid(),
            };
            await handler.Handle(first);
        }

        await ClearOutboxAsync();

        var replay = new CommitStockCommand(orderId, Guid.NewGuid(), sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<CommitStockCommandHandler>(scope.ServiceProvider);
            await handler.Handle(replay);
        }

        await AssertCommittedReplyAsync(orderId, replay, productId);
    }

    private async Task SeedHeldReservationAsync(int productId, Guid orderId, int quantity)
    {
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

        var command = new ReserveStockCommand(
            orderId,
            "customer-commit",
            [new ReserveStockItem(productId.ToString(CultureInfo.InvariantCulture), quantity, 1m)],
            "USD",
            causationId: Guid.NewGuid(),
            sagaId: Guid.NewGuid());

        using var scope = Factory.Services.CreateScope();
        var reserveHandler = ActivatorUtilities.CreateInstance<ReserveStockCommandHandler>(scope.ServiceProvider);
        await reserveHandler.Handle(command);
        await ClearOutboxAsync();
    }

    private async Task AssertCommittedReplyAsync(Guid orderId, CommitStockCommand command, int productId)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(StockCommittedEvent), StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(orderId, root.GetProperty(nameof(StockCommittedEvent.OrderId)).GetGuid());
        Assert.Equal(command.Id, root.GetProperty("CausationId").GetGuid());
        Assert.Equal(command.SagaId, root.GetProperty("SagaId").GetGuid());
        Assert.Equal(command.CorrelationId, root.GetProperty("CorrelationId").GetGuid());

        var items = root.GetProperty(nameof(StockCommittedEvent.Items));
        Assert.True(items.GetArrayLength() >= 1);
        Assert.Contains(items.EnumerateArray(),
            item => item.GetProperty(nameof(CommittedItem.ProductId)).GetInt32() == productId);
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
