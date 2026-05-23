using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.IntegrationEvents.EventHandlers;

namespace Payment.Tests.Api;

public class AuthorizePaymentCommandHandlerTests : IntegrationTestBase
{
    public AuthorizePaymentCommandHandlerTests(PaymentWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_AuthorizePaymentCommand_When_Handled_Then_AuthorizesAndEmitsAuthorizedReply()
    {
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var customerId = $"cust-{Guid.NewGuid():N}";

        PaymentContext.OrderCustomers.Add(new OrderCustomer
        {
            OrderId = orderId,
            CustomerId = customerId,
            ReceivedAt = DateTime.UtcNow,
        });
        await PaymentContext.SaveChangesAsync();

        var command = new AuthorizePaymentCommand(
            orderId,
            amount: 42.50m,
            currency: "USD",
            causationId: Guid.NewGuid(),
            sagaId: sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<AuthorizePaymentCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        PaymentContext.ChangeTracker.Clear();
        var payment = PaymentContext.Payments.Single(p => p.OrderId == orderId);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(customerId, payment.CustomerId);

        await AssertOutboxReplyAsync<PaymentAuthorizedEvent>(orderId, command, root =>
        {
            Assert.Equal(orderId, root.GetProperty(nameof(PaymentAuthorizedEvent.OrderId)).GetGuid());
            Assert.Equal(payment.PaymentId, root.GetProperty(nameof(PaymentAuthorizedEvent.PaymentId)).GetGuid());
            Assert.Equal(42.50m, root.GetProperty(nameof(PaymentAuthorizedEvent.Amount)).GetDecimal());
        });
    }

    [Fact]
    public async Task Given_PaymentAlreadyAuthorized_When_AuthorizeCommandReplayed_Then_EmitsAuthorizedReplyIdempotently()
    {
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var customerId = $"cust-{Guid.NewGuid():N}";

        PaymentContext.OrderCustomers.Add(new OrderCustomer
        {
            OrderId = orderId,
            CustomerId = customerId,
            ReceivedAt = DateTime.UtcNow,
        });
        var existing = Service.Domain.Payment.Create(
            paymentId: Guid.NewGuid(),
            orderId: orderId,
            customerId: customerId,
            amount: 42.50m,
            currency: "USD",
            createdAt: DateTime.UtcNow);
        existing.Authorize("PRE-EXISTING-REF", DateTime.UtcNow);
        existing.DequeueDomainEvents();
        PaymentContext.Payments.Add(existing);
        await PaymentContext.SaveChangesAsync();
        PaymentContext.ChangeTracker.Clear();

        var command = new AuthorizePaymentCommand(
            orderId,
            amount: 42.50m,
            currency: "USD",
            causationId: Guid.NewGuid(),
            sagaId: sagaId)
        {
            CorrelationId = Guid.NewGuid(),
        };

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<AuthorizePaymentCommandHandler>(scope.ServiceProvider);

        await handler.Handle(command);

        Assert.Single(PaymentContext.Payments.Where(p => p.OrderId == orderId));
        await AssertOutboxReplyAsync<PaymentAuthorizedEvent>(orderId, command, _ => { });
    }

    private async Task AssertOutboxReplyAsync<TReply>(
        Guid orderId,
        AuthorizePaymentCommand command,
        Action<JsonElement> additionalAssertions)
        where TReply : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var match = outboxEvents.Single(e =>
            e.EventType.Contains(typeof(TReply).Name, StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(match.Data);
        var root = document.RootElement;
        Assert.Equal(command.Id, root.GetProperty("CausationId").GetGuid());
        Assert.Equal(command.SagaId, root.GetProperty("SagaId").GetGuid());
        Assert.Equal(command.CorrelationId, root.GetProperty("CorrelationId").GetGuid());
        additionalAssertions(root);
    }
}
