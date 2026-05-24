using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using SharedCommands = ECommerce.Shared.IntegrationEvents.Commands;
using SliceHandlers = Payment.Service.Features.CapturePaymentCommand;

namespace Payment.Tests.Features.CapturePaymentCommand;

public class CapturePaymentCommandHandlerTests : IntegrationTestBase
{
    public CapturePaymentCommandHandlerTests(PaymentWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_AuthorizedPayment_When_CaptureCommandHandled_Then_CapturesAndEmitsCapturedReply()
    {
        var (paymentId, orderId) = await SeedAuthorizedPaymentAsync(amount: 75.00m);
        var command = NewCapture(orderId, paymentId, amount: 75.00m);

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<SliceHandlers.CapturePaymentCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        PaymentContext.ChangeTracker.Clear();
        var payment = PaymentContext.Payments.Single(p => p.PaymentId == paymentId);
        Assert.Equal(PaymentStatus.Captured, payment.Status);

        await AssertCapturedReplyAsync(paymentId, command);
    }

    [Fact]
    public async Task Given_AlreadyCapturedPayment_When_CaptureCommandReplayed_Then_EmitsCapturedReplyIdempotently()
    {
        var (paymentId, orderId) = await SeedAuthorizedPaymentAsync(amount: 12.34m);

        var first = NewCapture(orderId, paymentId, amount: 12.34m);
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<SliceHandlers.CapturePaymentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(first);
        }

        await ClearOutboxAsync();

        var replay = NewCapture(orderId, paymentId, amount: 12.34m);
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<SliceHandlers.CapturePaymentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(replay);
        }

        await AssertCapturedReplyAsync(paymentId, replay);
    }

    private async Task<(Guid PaymentId, Guid OrderId)> SeedAuthorizedPaymentAsync(decimal amount)
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
        payment.DequeueDomainEvents();
        PaymentContext.Payments.Add(payment);
        await PaymentContext.SaveChangesAsync();
        PaymentContext.ChangeTracker.Clear();
        return (paymentId, orderId);
    }

    private static SharedCommands.CapturePaymentCommand NewCapture(Guid orderId, Guid paymentId, decimal amount) =>
        new(orderId, paymentId, amount, causationId: Guid.NewGuid(), sagaId: Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid(),
        };

    private async Task AssertCapturedReplyAsync(Guid paymentId, SharedCommands.CapturePaymentCommand command)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(PaymentCapturedEvent), StringComparison.Ordinal)
            && e.Data.Contains(paymentId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(command.Id, root.GetProperty("CausationId").GetGuid());
        Assert.Equal(command.SagaId, root.GetProperty("SagaId").GetGuid());
        Assert.Equal(command.CorrelationId, root.GetProperty("CorrelationId").GetGuid());
        Assert.Equal(paymentId, root.GetProperty(nameof(PaymentCapturedEvent.PaymentId)).GetGuid());
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
