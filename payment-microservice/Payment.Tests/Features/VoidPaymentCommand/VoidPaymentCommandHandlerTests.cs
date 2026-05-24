using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using SharedCommands = ECommerce.Shared.IntegrationEvents.Commands;
using SliceHandlers = Payment.Service.Features.VoidPaymentCommand;

namespace Payment.Tests.Features.VoidPaymentCommand;

public class VoidPaymentCommandHandlerTests : IntegrationTestBase
{
    public VoidPaymentCommandHandlerTests(PaymentWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_AuthorizedPayment_When_VoidCommandHandled_Then_VoidsAndEmitsVoidedReply()
    {
        var (paymentId, orderId) = await SeedAuthorizedPaymentAsync(amount: 50.00m);
        var command = NewVoid(orderId);

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<SliceHandlers.VoidPaymentCommandHandler>(scope.ServiceProvider);
        await handler.Handle(command);

        PaymentContext.ChangeTracker.Clear();
        var payment = PaymentContext.Payments.Single(p => p.PaymentId == paymentId);
        Assert.Equal(PaymentStatus.Voided, payment.Status);

        await AssertVoidedReplyAsync(paymentId, command);
    }

    [Fact]
    public async Task Given_AlreadyVoidedPayment_When_VoidCommandReplayed_Then_EmitsVoidedReplyIdempotently()
    {
        var (paymentId, orderId) = await SeedAuthorizedPaymentAsync(amount: 12.50m);

        var first = NewVoid(orderId);
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<SliceHandlers.VoidPaymentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(first);
        }

        await ClearOutboxAsync();

        var replay = NewVoid(orderId);
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<SliceHandlers.VoidPaymentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(replay);
        }

        await AssertVoidedReplyAsync(paymentId, replay);
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

    private static SharedCommands.VoidPaymentCommand NewVoid(Guid orderId) =>
        new(orderId, "Saga compensation.", causationId: Guid.NewGuid(), sagaId: Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid(),
        };

    private async Task AssertVoidedReplyAsync(Guid paymentId, SharedCommands.VoidPaymentCommand command)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(nameof(PaymentVoidedEvent), StringComparison.Ordinal)
            && e.Data.Contains(paymentId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(command.Id, root.GetProperty("CausationId").GetGuid());
        Assert.Equal(command.SagaId, root.GetProperty("SagaId").GetGuid());
        Assert.Equal(command.CorrelationId, root.GetProperty("CorrelationId").GetGuid());
        Assert.Equal(paymentId, root.GetProperty(nameof(PaymentVoidedEvent.PaymentId)).GetGuid());
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
