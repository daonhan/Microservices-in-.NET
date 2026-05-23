using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.IntegrationEvents.EventHandlers;

namespace Shipping.Tests.Api;

public class CancelShipmentCommandHandlerTests : IntegrationTestBase
{
    public CancelShipmentCommandHandlerTests(ShippingWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_PendingShipment_When_CancelShipmentCommandHandled_Then_CancelsAndEmitsCancelledReply()
    {
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var customerId = $"cust-{Guid.NewGuid():N}";
        await SeedPendingShipmentAsync(orderId, customerId);

        var command = NewCancel(orderId, sagaId);

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<CancelShipmentCommandHandler>(scope.ServiceProvider);
        await handler.Handle(command);

        ShippingContext.ChangeTracker.Clear();
        var shipment = ShippingContext.Shipments.Single(s => s.OrderId == orderId);
        Assert.Equal(ShipmentStatus.Cancelled, shipment.Status);

        await AssertCancelledReplyAsync(orderId, customerId, command);
    }

    [Fact]
    public async Task Given_AlreadyCancelledShipment_When_CancelCommandReplayed_Then_EmitsCancelledReplyIdempotently()
    {
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var customerId = $"cust-{Guid.NewGuid():N}";
        await SeedPendingShipmentAsync(orderId, customerId);

        var first = NewCancel(orderId, sagaId);
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<CancelShipmentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(first);
        }

        await ClearOutboxAsync();

        var replay = NewCancel(orderId, sagaId);
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<CancelShipmentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(replay);
        }

        await AssertCancelledReplyAsync(orderId, customerId, replay);
    }

    private async Task SeedPendingShipmentAsync(Guid orderId, string customerId)
    {
        ShippingContext.OrderConfirmations.Add(new OrderConfirmation
        {
            OrderId = orderId,
            CustomerId = customerId,
            ReceivedAt = DateTime.UtcNow,
        });

        var shipment = Shipment.Create(
            id: Guid.NewGuid(),
            orderId: orderId,
            customerId: customerId,
            warehouseId: 1,
            createdAt: DateTime.UtcNow);
        shipment.AddLine(productId: 801, quantity: 2);
        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();
        ShippingContext.ChangeTracker.Clear();
    }

    private static CancelShipmentCommand NewCancel(Guid orderId, Guid sagaId) =>
        new(orderId, "Saga compensation.", causationId: Guid.NewGuid(), sagaId: sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };

    private async Task AssertCancelledReplyAsync(Guid orderId, string customerId, CancelShipmentCommand command)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(ShipmentCancelledEvent), StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(orderId, root.GetProperty(nameof(ShipmentCancelledEvent.OrderId)).GetGuid());
        Assert.Equal(customerId, root.GetProperty(nameof(ShipmentCancelledEvent.CustomerId)).GetString());
        Assert.Equal(command.Id, root.GetProperty("CausationId").GetGuid());
        Assert.Equal(command.SagaId, root.GetProperty("SagaId").GetGuid());
        Assert.Equal(command.CorrelationId, root.GetProperty("CorrelationId").GetGuid());
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
