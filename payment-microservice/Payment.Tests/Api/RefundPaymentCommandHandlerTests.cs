using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Features.RefundPaymentCommand;

namespace Payment.Tests.Api;

public class RefundPaymentCommandHandlerTests : IntegrationTestBase
{
    public RefundPaymentCommandHandlerTests(PaymentWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_CapturedPayment_When_RefundCommandHandled_Then_RefundsAndEmitsRefundedReply()
    {
        var (paymentId, orderId) = await SeedCapturedPaymentAsync(amount: 75.00m);
        var command = NewRefund(orderId, amount: 75.00m);

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<RefundPaymentCommandHandler>(scope.ServiceProvider);
        await handler.Handle(command);

        PaymentContext.ChangeTracker.Clear();
        var payment = PaymentContext.Payments.Single(p => p.PaymentId == paymentId);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);

        await AssertRefundedReplyAsync(paymentId, command);
    }

    [Fact]
    public async Task Given_AlreadyRefundedPayment_When_RefundCommandReplayed_Then_EmitsRefundedReplyIdempotently()
    {
        var (paymentId, orderId) = await SeedCapturedPaymentAsync(amount: 9.99m);

        var first = NewRefund(orderId, amount: 9.99m);
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<RefundPaymentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(first);
        }

        await ClearOutboxAsync();

        var replay = NewRefund(orderId, amount: 9.99m);
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<RefundPaymentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(replay);
        }

        await AssertRefundedReplyAsync(paymentId, replay);
    }

    private async Task<(Guid PaymentId, Guid OrderId)> SeedCapturedPaymentAsync(decimal amount)
    {
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var payment = Service.Domain.Payment.Create(
            paymentId: paymentId,
            orderId: orderId,
            customerId: $"cust-{Guid.NewGuid():N}",
            amount: amount,
            currency: "USD",
            createdAt: now);
        payment.Authorize($"PRE-{Guid.NewGuid():N}", now);
        payment.Capture(now);
        payment.DequeueDomainEvents();
        PaymentContext.Payments.Add(payment);
        await PaymentContext.SaveChangesAsync();
        PaymentContext.ChangeTracker.Clear();
        return (paymentId, orderId);
    }

    private static RefundPaymentCommand NewRefund(Guid orderId, decimal amount) =>
        new(orderId, amount, causationId: Guid.NewGuid(), sagaId: Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid(),
        };

    private async Task AssertRefundedReplyAsync(Guid paymentId, RefundPaymentCommand command)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(PaymentRefundedEvent), StringComparison.Ordinal)
            && e.Data.Contains(paymentId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(command.Id, root.GetProperty("CausationId").GetGuid());
        Assert.Equal(command.SagaId, root.GetProperty("SagaId").GetGuid());
        Assert.Equal(command.CorrelationId, root.GetProperty("CorrelationId").GetGuid());
        Assert.Equal(paymentId, root.GetProperty(nameof(PaymentRefundedEvent.PaymentId)).GetGuid());
        Assert.Equal(command.Amount, root.GetProperty(nameof(PaymentRefundedEvent.Amount)).GetDecimal());
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
