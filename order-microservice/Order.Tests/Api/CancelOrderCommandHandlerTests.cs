using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.Extensions.DependencyInjection;
using Order.Service.IntegrationEvents.EventHandlers;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Tests.Api;

public class CancelOrderCommandHandlerTests : IntegrationTestBase
{
    public CancelOrderCommandHandlerTests(OrderWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_PendingOrder_When_CancelCommandHandled_Then_TransitionsToCancelledAndEmitsReply()
    {
        var order = new Service.Models.Order { CustomerId = $"cust-{Guid.NewGuid():N}" };
        order.AddOrderProduct("101", 2);
        OrderContext.Orders.Add(order);
        await OrderContext.SaveChangesAsync();
        OrderContext.ChangeTracker.Clear();

        var command = NewCancel(order.OrderId);

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<CancelOrderCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        OrderContext.ChangeTracker.Clear();
        var stored = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, stored.Status);

        await AssertOrderCancelledReplyAsync(order.OrderId, order.CustomerId, command);
    }

    [Fact]
    public async Task Given_AlreadyCancelledOrder_When_CancelCommandReplayed_Then_EmitsReplyIdempotently()
    {
        var order = new Service.Models.Order { CustomerId = $"cust-{Guid.NewGuid():N}" };
        order.AddOrderProduct("101", 2);
        order.TryCancel();
        order.DequeueDomainEvents();
        OrderContext.Orders.Add(order);
        await OrderContext.SaveChangesAsync();
        OrderContext.ChangeTracker.Clear();

        var command = NewCancel(order.OrderId);

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<CancelOrderCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        OrderContext.ChangeTracker.Clear();
        var stored = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, stored.Status);

        await AssertOrderCancelledReplyAsync(order.OrderId, order.CustomerId, command);
    }

    private static CancelOrderCommand NewCancel(Guid orderId) =>
        new(orderId, causationId: Guid.NewGuid(), sagaId: Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid(),
        };

    private async Task AssertOrderCancelledReplyAsync(Guid orderId, string customerId, CancelOrderCommand command)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(OrderCancelledEvent), StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(orderId, root.GetProperty(nameof(OrderCancelledEvent.OrderId)).GetGuid());
        Assert.Equal(customerId, root.GetProperty(nameof(OrderCancelledEvent.CustomerId)).GetString());
        Assert.Equal(command.Id, root.GetProperty("CausationId").GetGuid());
        Assert.Equal(command.SagaId, root.GetProperty("SagaId").GetGuid());
        Assert.Equal(command.CorrelationId, root.GetProperty("CorrelationId").GetGuid());
    }
}
