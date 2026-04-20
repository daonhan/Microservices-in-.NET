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
}
