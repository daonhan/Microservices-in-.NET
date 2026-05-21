using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.Extensions.DependencyInjection;
using Order.Service.Contracts.Integration;
using Order.Service.Domain;
using Order.Service.Features.ConfirmOrder;

namespace Order.Tests.Features.ConfirmOrder;

public class ConfirmOrderCommandHandlerTests : IntegrationTestBase
{
    public ConfirmOrderCommandHandlerTests(OrderWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_PendingOrder_When_ConfirmCommandHandled_Then_TransitionsToConfirmedAndEmitsReply()
    {
        var order = new Service.Domain.Order { CustomerId = $"cust-{Guid.NewGuid():N}" };
        order.AddOrderProduct("101", 2);
        OrderContext.Orders.Add(order);
        await OrderContext.SaveChangesAsync();
        OrderContext.ChangeTracker.Clear();

        var command = NewConfirm(order.OrderId);

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<ConfirmOrderCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        OrderContext.ChangeTracker.Clear();
        var stored = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Confirmed, stored.Status);

        await AssertOrderConfirmedReplyAsync(order.OrderId, order.CustomerId, command);
    }

    [Fact]
    public async Task Given_AlreadyConfirmedOrder_When_ConfirmCommandReplayed_Then_EmitsReplyIdempotently()
    {
        var order = new Service.Domain.Order { CustomerId = $"cust-{Guid.NewGuid():N}" };
        order.AddOrderProduct("101", 2);
        order.TryConfirm();
        order.DequeueDomainEvents();
        OrderContext.Orders.Add(order);
        await OrderContext.SaveChangesAsync();
        OrderContext.ChangeTracker.Clear();

        var command = NewConfirm(order.OrderId);

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<ConfirmOrderCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        OrderContext.ChangeTracker.Clear();
        var stored = OrderContext.Orders.Single(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Confirmed, stored.Status);

        await AssertOrderConfirmedReplyAsync(order.OrderId, order.CustomerId, command);
    }

    private static ConfirmOrderCommand NewConfirm(Guid orderId) =>
        new(orderId, causationId: Guid.NewGuid(), sagaId: Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid(),
        };

    private async Task AssertOrderConfirmedReplyAsync(Guid orderId, string customerId, ConfirmOrderCommand command)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(OrderConfirmedEvent), StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(orderId, root.GetProperty(nameof(OrderConfirmedEvent.OrderId)).GetGuid());
        Assert.Equal(customerId, root.GetProperty(nameof(OrderConfirmedEvent.CustomerId)).GetString());
        Assert.Equal(command.Id, root.GetProperty("CausationId").GetGuid());
        Assert.Equal(command.SagaId, root.GetProperty("SagaId").GetGuid());
        Assert.Equal(command.CorrelationId, root.GetProperty("CorrelationId").GetGuid());
    }
}
