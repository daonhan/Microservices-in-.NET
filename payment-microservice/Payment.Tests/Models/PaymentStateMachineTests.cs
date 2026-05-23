using Payment.Service.Domain;
using Payment.Service.Domain.Events;

namespace Payment.Tests.Models;

public class PaymentStateMachineTests
{
    private static Service.Domain.Payment NewPending()
    {
        return Service.Domain.Payment.Create(
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

        var authorized = payment.Authorize("ref-1", occurredAt);

        Assert.True(authorized);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("ref-1", payment.ProviderReference);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var domainEvent = Assert.Single(payment.DomainEvents.OfType<PaymentAuthorizedDomainEvent>());
        Assert.Equal(payment.PaymentId, domainEvent.PaymentId);
        Assert.Equal(payment.OrderId, domainEvent.OrderId);
        Assert.Equal(payment.CustomerId, domainEvent.CustomerId);
        Assert.Equal(payment.Amount, domainEvent.Amount);
        Assert.Equal(payment.Currency, domainEvent.Currency);
    }

    [Fact]
    public void Authorize_WhenAlreadyAuthorized_IsNoOp()
    {
        var payment = NewPending();
        payment.Authorize("ref-1", DateTime.UtcNow);
        payment.DequeueDomainEvents();
        var updatedAt = payment.UpdatedAt;

        var authorized = payment.Authorize("ref-2", DateTime.UtcNow.AddMinutes(1));

        Assert.False(authorized);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("ref-1", payment.ProviderReference);
        Assert.Equal(updatedAt, payment.UpdatedAt);
        Assert.Empty(payment.DequeueDomainEvents());
    }

    [Fact]
    public void Fail_FromPending_TransitionsToFailed()
    {
        var payment = NewPending();
        var occurredAt = DateTime.UtcNow;

        var failed = payment.Fail("Card declined by issuer", occurredAt);

        Assert.True(failed);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var domainEvent = Assert.Single(payment.DomainEvents.OfType<PaymentFailedDomainEvent>());
        Assert.Equal(payment.PaymentId, domainEvent.PaymentId);
        Assert.Equal(payment.OrderId, domainEvent.OrderId);
        Assert.Equal(payment.CustomerId, domainEvent.CustomerId);
        Assert.Equal("Card declined by issuer", domainEvent.Reason);
    }

    [Fact]
    public void Fail_WhenAlreadyFailed_IsNoOp()
    {
        var payment = NewPending();
        payment.Fail("Card declined by issuer", DateTime.UtcNow);
        payment.DequeueDomainEvents();
        var updatedAt = payment.UpdatedAt;

        var failed = payment.Fail("Gateway redelivery", DateTime.UtcNow.AddMinutes(1));

        Assert.False(failed);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(updatedAt, payment.UpdatedAt);
        Assert.Empty(payment.DequeueDomainEvents());
    }

    [Fact]
    public void Capture_FromAuthorized_TransitionsToCaptured()
    {
        var payment = NewPending();
        payment.Authorize("ref-1", DateTime.UtcNow);
        var occurredAt = DateTime.UtcNow;

        var captured = payment.Capture(occurredAt);

        Assert.True(captured);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var capturedEvent = Assert.Single(payment.DomainEvents.OfType<PaymentCapturedDomainEvent>());
        Assert.Equal(payment.PaymentId, capturedEvent.PaymentId);
        Assert.Equal(payment.OrderId, capturedEvent.OrderId);
        Assert.Equal(payment.Amount, capturedEvent.Amount);
    }

    [Fact]
    public void Capture_WhenAlreadyCaptured_IsNoOp()
    {
        var payment = NewPending();
        payment.Authorize("ref-1", DateTime.UtcNow);
        payment.Capture(DateTime.UtcNow);
        payment.DequeueDomainEvents();
        var updatedAt = payment.UpdatedAt;

        var captured = payment.Capture(DateTime.UtcNow.AddMinutes(1));

        Assert.False(captured);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(updatedAt, payment.UpdatedAt);
        Assert.Empty(payment.DequeueDomainEvents());
    }

    [Fact]
    public void Refund_FromCaptured_TransitionsToRefunded()
    {
        var payment = NewPending();
        payment.Authorize("ref-1", DateTime.UtcNow);
        payment.Capture(DateTime.UtcNow);
        var occurredAt = DateTime.UtcNow;

        var refunded = payment.Refund(payment.Amount, occurredAt);

        Assert.True(refunded);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var domainEvent = Assert.Single(payment.DomainEvents.OfType<PaymentRefundedDomainEvent>());
        Assert.Equal(payment.PaymentId, domainEvent.PaymentId);
        Assert.Equal(payment.OrderId, domainEvent.OrderId);
        Assert.Equal(payment.Amount, domainEvent.Amount);
    }

    [Fact]
    public void Refund_WhenAlreadyRefunded_IsNoOp()
    {
        var payment = NewPending();
        payment.Authorize("ref-1", DateTime.UtcNow);
        payment.Capture(DateTime.UtcNow);
        payment.Refund(payment.Amount, DateTime.UtcNow);
        payment.DequeueDomainEvents();
        var updatedAt = payment.UpdatedAt;

        var refunded = payment.Refund(payment.Amount, DateTime.UtcNow.AddMinutes(1));

        Assert.False(refunded);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(updatedAt, payment.UpdatedAt);
        Assert.Empty(payment.DequeueDomainEvents());
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
    public void Fail_FromNonPending_Throws(PaymentStatus current)
    {
        var payment = MoveTo(current);
        Assert.Throws<InvalidOperationException>(() => payment.Fail("reason", DateTime.UtcNow));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
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
    [InlineData(PaymentStatus.Failed)]
    public void Refund_FromNonCaptured_Throws(PaymentStatus current)
    {
        var payment = MoveTo(current);
        Assert.Throws<InvalidOperationException>(() => payment.Refund(payment.Amount, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    public void Void_FromPendingOrAuthorized_TransitionsToVoided(PaymentStatus current)
    {
        var payment = MoveTo(current);
        var occurredAt = DateTime.UtcNow;

        var voided = payment.Void("Order cancelled", occurredAt);

        Assert.True(voided);
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal(occurredAt, payment.UpdatedAt);

        var domainEvent = Assert.Single(payment.DomainEvents.OfType<PaymentVoidedDomainEvent>());
        Assert.Equal(payment.PaymentId, domainEvent.PaymentId);
        Assert.Equal(payment.OrderId, domainEvent.OrderId);
        Assert.Equal(payment.CustomerId, domainEvent.CustomerId);
        Assert.Equal("Order cancelled", domainEvent.Reason);
    }

    [Fact]
    public void Void_WhenAlreadyVoided_IsNoOp()
    {
        var payment = MoveTo(PaymentStatus.Authorized);
        payment.Void("Order cancelled", DateTime.UtcNow);
        payment.DequeueDomainEvents();
        var updatedAt = payment.UpdatedAt;

        var voided = payment.Void("Order cancelled redelivery", DateTime.UtcNow.AddMinutes(1));

        Assert.False(voided);
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal(updatedAt, payment.UpdatedAt);
        Assert.Empty(payment.DequeueDomainEvents());
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

    private static Service.Domain.Payment MoveTo(PaymentStatus target)
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
            case PaymentStatus.Voided:
                payment.Void("voided", DateTime.UtcNow);
                return payment;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }
}
