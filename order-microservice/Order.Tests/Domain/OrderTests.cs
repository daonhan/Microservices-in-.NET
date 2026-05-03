using Order.Service.Models;

namespace Order.Tests.Domain;

public class OrderTests
{
    [Fact]
    public void NewOrder_StartsInPendingStockStatus()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };

        Assert.Equal(OrderStatus.PendingStock, order.Status);
    }

    [Fact]
    public void TryConfirm_FromPending_TransitionsToConfirmed()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };

        var confirmed = order.TryConfirm();

        Assert.True(confirmed);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void TryConfirm_WhenAlreadyConfirmed_IsNoOp()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        order.TryConfirm();

        var second = order.TryConfirm();

        Assert.False(second);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void TryCancel_FromPending_TransitionsToCancelled()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };

        var cancelled = order.TryCancel();

        Assert.True(cancelled);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void TryCancel_WhenAlreadyCancelled_IsNoOp()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        order.TryCancel();

        var second = order.TryCancel();

        Assert.False(second);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void TryConfirm_FromCancelled_DoesNotChangeStatus()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        order.TryCancel();

        var result = order.TryConfirm();

        Assert.False(result);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void TryConfirm_RaisesOrderConfirmedDomainEvent()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };

        order.TryConfirm();
        var events = order.DequeueDomainEvents();

        var confirmed = Assert.IsType<OrderConfirmedDomainEvent>(Assert.Single(events));
        Assert.Equal(order.OrderId, confirmed.OrderId);
        Assert.Equal(order.CustomerId, confirmed.CustomerId);
    }

    [Fact]
    public void TryCancel_RaisesOrderCancelledDomainEvent()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };

        order.TryCancel();
        var events = order.DequeueDomainEvents();

        var cancelled = Assert.IsType<OrderCancelledDomainEvent>(Assert.Single(events));
        Assert.Equal(order.OrderId, cancelled.OrderId);
        Assert.Equal(order.CustomerId, cancelled.CustomerId);
    }

    [Fact]
    public void TryConfirm_WhenNoOp_DoesNotRaiseDomainEvent()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        order.TryConfirm();
        order.DequeueDomainEvents();

        order.TryConfirm();

        Assert.Empty(order.DequeueDomainEvents());
    }

    [Fact]
    public void Submit_RaisesOrderCreatedDomainEventWithItemsAndCurrency()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        order.AddOrderProduct("p-1", 3);
        order.AddOrderProduct("p-2", 1);

        order.Submit(new Dictionary<string, decimal> { { "p-1", 5m }, { "p-2", 12.5m } }, currency: "EUR");

        var created = Assert.IsType<OrderCreatedDomainEvent>(Assert.Single(order.DequeueDomainEvents()));
        Assert.Equal(order.OrderId, created.OrderId);
        Assert.Equal("c-1", created.CustomerId);
        Assert.Equal("EUR", created.Currency);
        Assert.Equal(2, created.Items.Count);
        Assert.Contains(created.Items, i => i.ProductId == "p-1" && i.Quantity == 3 && i.UnitPrice == 5m);
        Assert.Contains(created.Items, i => i.ProductId == "p-2" && i.Quantity == 1 && i.UnitPrice == 12.5m);
    }

    [Fact]
    public void DequeueDomainEvents_ClearsTheQueue()
    {
        var order = new Service.Models.Order { CustomerId = "c-1" };
        order.TryConfirm();

        order.DequeueDomainEvents();

        Assert.Empty(order.DomainEvents);
    }
}
