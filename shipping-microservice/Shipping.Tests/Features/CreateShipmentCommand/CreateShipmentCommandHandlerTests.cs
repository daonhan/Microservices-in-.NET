using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Features.CreateShipmentCommand;
using SagaCreateShipmentCommand = ECommerce.Shared.IntegrationEvents.Commands.CreateShipmentCommand;

namespace Shipping.Tests.Features.CreateShipmentCommand;

public class CreateShipmentCommandHandlerTests : IntegrationTestBase
{
    public CreateShipmentCommandHandlerTests(ShippingWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_CreateShipmentCommand_When_Handled_Then_CreatesShipmentAndEmitsCreatedReply()
    {
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var customerId = $"cust-{Guid.NewGuid():N}";

        ShippingContext.OrderConfirmations.Add(new OrderConfirmation
        {
            OrderId = orderId,
            CustomerId = customerId,
            ReceivedAt = DateTime.UtcNow,
        });
        await ShippingContext.SaveChangesAsync();

        var command = new SagaCreateShipmentCommand(
            orderId,
            [new CreateShipmentItem(ProductId: 701, WarehouseId: 1, Quantity: 4)],
            causationId: Guid.NewGuid(),
            sagaId: sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<CreateShipmentCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        ShippingContext.ChangeTracker.Clear();
        var shipments = ShippingContext.Shipments.Where(s => s.OrderId == orderId).ToList();
        Assert.Single(shipments);
        Assert.Equal(ShipmentStatus.Pending, shipments[0].Status);

        await AssertCreatedReplyAsync(orderId, customerId, command);
    }

    [Fact]
    public async Task Given_ShipmentAlreadyCreated_When_CreateCommandReplayed_Then_NoNewShipmentNorReplyEmitted()
    {
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var customerId = $"cust-{Guid.NewGuid():N}";

        ShippingContext.OrderConfirmations.Add(new OrderConfirmation
        {
            OrderId = orderId,
            CustomerId = customerId,
            ReceivedAt = DateTime.UtcNow,
        });
        await ShippingContext.SaveChangesAsync();

        var first = new SagaCreateShipmentCommand(
            orderId,
            [new CreateShipmentItem(702, 1, 2)],
            causationId: Guid.NewGuid(),
            sagaId: sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<CreateShipmentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(first);
        }

        await ClearOutboxAsync();

        var replay = new SagaCreateShipmentCommand(
            orderId,
            [new CreateShipmentItem(702, 1, 2)],
            causationId: Guid.NewGuid(),
            sagaId: sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<CreateShipmentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(replay);
        }

        ShippingContext.ChangeTracker.Clear();
        Assert.Single(ShippingContext.Shipments.Where(s => s.OrderId == orderId));

        using var verifyScope = Factory.Services.CreateScope();
        var outboxStore = verifyScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.DoesNotContain(outboxEvents, e =>
            e.EventType.Contains(nameof(ShipmentCreatedEvent), StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task AssertCreatedReplyAsync(Guid orderId, string customerId, SagaCreateShipmentCommand command)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(ShipmentCreatedEvent), StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(orderId, root.GetProperty(nameof(ShipmentCreatedEvent.OrderId)).GetGuid());
        Assert.Equal(customerId, root.GetProperty(nameof(ShipmentCreatedEvent.CustomerId)).GetString());
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
