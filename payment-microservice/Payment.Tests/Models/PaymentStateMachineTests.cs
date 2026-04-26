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
    }

    [Fact]
    public void Fail_FromPending_TransitionsToFailed()
    {
        var payment = NewPending();
        var occurredAt = DateTime.UtcNow;

        payment.Fail(occurredAt);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);
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
    }

    [Fact]
    public void Refund_FromCaptured_TransitionsToRefunded()
    {
        var payment = NewPending();
        payment.Authorize("ref-1", DateTime.UtcNow);
        payment.Capture(DateTime.UtcNow);
        var occurredAt = DateTime.UtcNow;

        payment.Refund(occurredAt);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);
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
        Assert.Throws<InvalidOperationException>(() => payment.Fail(DateTime.UtcNow));
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
        Assert.Throws<InvalidOperationException>(() => payment.Refund(DateTime.UtcNow));
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
                payment.Refund(DateTime.UtcNow);
                return payment;
            case PaymentStatus.Failed:
                payment.Fail(DateTime.UtcNow);
                return payment;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }
}
