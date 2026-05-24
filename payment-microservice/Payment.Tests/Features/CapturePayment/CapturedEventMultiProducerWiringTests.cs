using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Contracts.Integration;
using Payment.Service.Features.CapturePaymentCommand;

namespace Payment.Tests.Features.CapturePayment;

// Pins the multi-producer wiring contract: HTTP CapturePayment and saga CapturePaymentCommand
// both raise PaymentCapturedDomainEvent and must route through a single
// PaymentCapturedIntegrationMap registration (owned by the HTTP slice). Regressing to a
// duplicate registration in the saga slice would cause the outbox to publish two integration
// events per domain event.
public class CapturedEventMultiProducerWiringTests : IntegrationTestBase
{
    public CapturedEventMultiProducerWiringTests(PaymentWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task PaymentCapturedEvent_FromHttpAndFromSagaCommand_ProduceIdenticallyShapedOutboxEntries()
    {
        var httpPaymentId = await SeedAuthorizedPaymentAsync(amount: 75.00m);
        var httpResponse = await CreateAuthenticatedClient().PostAsync(
            $"/{httpPaymentId}/capture",
            content: null);
        httpResponse.EnsureSuccessStatusCode();

        var (sagaPaymentId, sagaOrderId) = await SeedAuthorizedPaymentWithOrderAsync(amount: 75.00m);
        var sagaCommand = new ECommerce.Shared.IntegrationEvents.Commands.CapturePaymentCommand(
            sagaOrderId, sagaPaymentId, amount: 75.00m,
            causationId: Guid.NewGuid(), sagaId: Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid(),
        };
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<CapturePaymentCommandHandler>(scope.ServiceProvider);
            await handler.Handle(sagaCommand);
        }

        var httpEntry = await GetSingleOutboxEntryAsync(nameof(PaymentCapturedEvent), httpPaymentId);
        var sagaEntry = await GetSingleOutboxEntryAsync(nameof(PaymentCapturedEvent), sagaPaymentId);

        Assert.Equal(httpEntry.EventType, sagaEntry.EventType);

        using var httpDoc = JsonDocument.Parse(httpEntry.Data);
        using var sagaDoc = JsonDocument.Parse(sagaEntry.Data);
        AssertSamePropertyShape(httpDoc.RootElement, sagaDoc.RootElement);

        Assert.Equal(75.00m, httpDoc.RootElement.GetProperty(nameof(PaymentCapturedEvent.Amount)).GetDecimal());
        Assert.Equal(75.00m, sagaDoc.RootElement.GetProperty(nameof(PaymentCapturedEvent.Amount)).GetDecimal());
    }

    private async Task<Guid> SeedAuthorizedPaymentAsync(decimal amount)
    {
        var (paymentId, _) = await SeedAuthorizedPaymentWithOrderAsync(amount);
        return paymentId;
    }

    private async Task<(Guid PaymentId, Guid OrderId)> SeedAuthorizedPaymentWithOrderAsync(decimal amount)
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
