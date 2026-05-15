using Payment.Service.Models;

namespace Payment.Tests.Models;

public class PaymentStateMachineTests
{
    private static Service.Models.Payment NewPending()
    {
        return Service.Models.Payment.Create(
            paymentId: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            customerId: "cust-1",
            amount: 50.00m,
            currency: "USD",
            createdAt: new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Authorize_FromPending_TransitionsToAuthorized()
    {
        var payment = NewPending();
        var occurredAt = DateTime.UtcNow;

        payment.Authorize("ref-1", occurredAt);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("ref-1", payment.ProviderReference);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var authorized = Assert.Single(payment.DomainEvents.OfType<PaymentAuthorizedDomainEvent>());
        Assert.Equal(payment.PaymentId, authorized.PaymentId);
        Assert.Equal(payment.OrderId, authorized.OrderId);
        Assert.Equal(payment.CustomerId, authorized.CustomerId);
        Assert.Equal(payment.Amount, authorized.Amount);
        Assert.Equal(payment.Currency, authorized.Currency);
    }

    [Fact]
    public void Fail_FromPending_TransitionsToFailed()
    {
        var payment = NewPending();
        var occurredAt = DateTime.UtcNow;

        payment.Fail("Card declined by issuer", occurredAt);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var failed = Assert.Single(payment.DomainEvents.OfType<PaymentFailedDomainEvent>());
        Assert.Equal(payment.PaymentId, failed.PaymentId);
        Assert.Equal(payment.OrderId, failed.OrderId);
        Assert.Equal(payment.CustomerId, failed.CustomerId);
        Assert.Equal("Card declined by issuer", failed.Reason);
    }

    [Fact]
    public void Capture_FromAuthorized_TransitionsToCaptured()
    {
        var payment = NewPending();
        payment.Authorize("ref-1", DateTime.UtcNow);
        var occurredAt = DateTime.UtcNow;

        payment.Capture(occurredAt);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var domainEvent = Assert.Single(payment.DomainEvents);
        var captured = Assert.IsType<PaymentCapturedDomainEvent>(domainEvent);
        Assert.Equal(payment.PaymentId, captured.PaymentId);
        Assert.Equal(payment.OrderId, captured.OrderId);
        Assert.Equal(payment.Amount, captured.Amount);
    }

    [Fact]
    public void Refund_FromCaptured_TransitionsToRefunded()
    {
        var payment = NewPending();
        payment.Authorize("ref-1", DateTime.UtcNow);
        payment.Capture(DateTime.UtcNow);
        var occurredAt = DateTime.UtcNow;

        payment.Refund(payment.Amount, occurredAt);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var refunded = Assert.Single(payment.DomainEvents.OfType<PaymentRefundedDomainEvent>());
        Assert.Equal(payment.PaymentId, refunded.PaymentId);
        Assert.Equal(payment.OrderId, refunded.OrderId);
        Assert.Equal(payment.Amount, refunded.Amount);
    }

    [Fact]
    public void Refund_WithPartialAmount_RaisesRefundedEventWithThatAmount()
    {
        var payment = NewPending();
        payment.Authorize("ref-1", DateTime.UtcNow);
        payment.Capture(DateTime.UtcNow);

        payment.Refund(20.00m, DateTime.UtcNow);

        var refunded = Assert.Single(payment.DomainEvents.OfType<PaymentRefundedDomainEvent>());
        Assert.Equal(20.00m, refunded.Amount);
    }

    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Failed)]
    public void Authorize_FromNonPending_Throws(PaymentStatus current)
    {
        var payment = MoveTo(current);
        Assert.Throws<InvalidOperationException>(() => payment.Authorize("ref", DateTime.UtcNow));
    }

    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Failed)]
    public void Fail_FromNonPending_Throws(PaymentStatus current)
    {
        var payment = MoveTo(current);
        Assert.Throws<InvalidOperationException>(() => payment.Fail("reason", DateTime.UtcNow));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Failed)]
    public void Capture_FromNonAuthorized_Throws(PaymentStatus current)
    {
        var payment = MoveTo(current);
        Assert.Throws<InvalidOperationException>(() => payment.Capture(DateTime.UtcNow));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Failed)]
    public void Refund_FromNonCaptured_Throws(PaymentStatus current)
    {
        var payment = MoveTo(current);
        Assert.Throws<InvalidOperationException>(() => payment.Refund(payment.Amount, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    public void Void_FromPendingOrAuthorized_TransitionsToFailed(PaymentStatus current)
    {
        var payment = MoveTo(current);
        var occurredAt = DateTime.UtcNow;

        payment.Void("Order cancelled", occurredAt);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var failed = Assert.Single(payment.DomainEvents.OfType<PaymentFailedDomainEvent>());
        Assert.Equal(payment.PaymentId, failed.PaymentId);
        Assert.Equal(payment.OrderId, failed.OrderId);
        Assert.Equal(payment.CustomerId, failed.CustomerId);
        Assert.Equal("Order cancelled", failed.Reason);
    }

    [Theory]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Failed)]
    public void Void_FromTerminal_Throws(PaymentStatus current)
    {
        var payment = MoveTo(current);
        Assert.Throws<InvalidOperationException>(() => payment.Void("reason", DateTime.UtcNow));
    }

    private static Service.Models.Payment MoveTo(PaymentStatus target)
    {
        var payment = NewPending();
        switch (target)
        {
            case PaymentStatus.Pending:
                return payment;
            case PaymentStatus.Authorized:
                payment.Authorize("ref", DateTime.UtcNow);
                return payment;
            case PaymentStatus.Captured:
                payment.Authorize("ref", DateTime.UtcNow);
                payment.Capture(DateTime.UtcNow);
                return payment;
            case PaymentStatus.Refunded:
                payment.Authorize("ref", DateTime.UtcNow);
                payment.Capture(DateTime.UtcNow);
                payment.Refund(payment.Amount, DateTime.UtcNow);
                return payment;
            case PaymentStatus.Failed:
                payment.Fail("reason", DateTime.UtcNow);
                return payment;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }
}
