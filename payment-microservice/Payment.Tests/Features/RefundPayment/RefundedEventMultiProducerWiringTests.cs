using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Contracts.Integration;
using Payment.Service.Features.RefundPayment;
using Payment.Service.Features.RefundPaymentCommand;

namespace Payment.Tests.Features.RefundPayment;

// Pins the multi-producer wiring contract: HTTP RefundPayment and saga RefundPaymentCommand
// both raise PaymentRefundedDomainEvent and must route through a single
// PaymentRefundedIntegrationMap registration (owned by the HTTP slice). Regressing to a
// duplicate registration in the saga slice would cause the outbox to publish two integration
// events per domain event.
public class RefundedEventMultiProducerWiringTests : IntegrationTestBase
{
    public RefundedEventMultiProducerWiringTests(PaymentWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task PaymentRefundedEvent_FromHttpAndFromSagaCommand_ProduceIdenticallyShapedOutboxEntries()
    {
        var httpPaymentId = await SeedCapturedPaymentAsync(amount: 9.99m);
        var httpResponse = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{httpPaymentId}/refund",
            new RefundPaymentRequest(Amount: null));
        httpResponse.EnsureSuccessStatusCode();

        var (sagaPaymentId, sagaOrderId) = await SeedCapturedPaymentWithOrderAsync(amount: 9.99m);
        var sagaCommand = new ECommerce.Shared.IntegrationEvents.Commands.RefundPaymentCommand(
            sagaOrderId, amount: 9.99m,
            causationId: Guid.NewGuid(), sagaId: Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid(),
        };
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<RefundPaymentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(sagaCommand);
        }

        var httpEntry = await GetSingleOutboxEntryAsync(nameof(PaymentRefundedEvent), httpPaymentId);
        var sagaEntry = await GetSingleOutboxEntryAsync(nameof(PaymentRefundedEvent), sagaPaymentId);

        Assert.Equal(httpEntry.EventType, sagaEntry.EventType);

        using var httpDoc = JsonDocument.Parse(httpEntry.Data);
        using var sagaDoc = JsonDocument.Parse(sagaEntry.Data);
        AssertSamePropertyShape(httpDoc.RootElement, sagaDoc.RootElement);

        Assert.Equal(9.99m, httpDoc.RootElement.GetProperty(nameof(PaymentRefundedEvent.Amount)).GetDecimal());
        Assert.Equal(9.99m, sagaDoc.RootElement.GetProperty(nameof(PaymentRefundedEvent.Amount)).GetDecimal());
    }

    private async Task<Guid> SeedCapturedPaymentAsync(decimal amount)
    {
        var (paymentId, _) = await SeedCapturedPaymentWithOrderAsync(amount);
        return paymentId;
    }

    private async Task<(Guid PaymentId, Guid OrderId)> SeedCapturedPaymentWithOrderAsync(decimal amount)
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

    private async Task<OutboxEvent> GetSingleOutboxEntryAsync(string eventTypeName, Guid paymentId)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        return outboxEvents.Single(e =>
            e.EventType.Contains(eventTypeName, StringComparison.Ordinal) &&
            e.Data.Contains(paymentId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertSamePropertyShape(JsonElement left, JsonElement right)
    {
        var leftKeys = left.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var rightKeys = right.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(leftKeys, rightKeys);
    }
}
