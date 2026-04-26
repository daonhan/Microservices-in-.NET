using System.Diagnostics;
using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Infrastructure.Data;
using Payment.Service.Infrastructure.Data.EntityFramework;
using Payment.Service.Infrastructure.Gateways;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;
using Payment.Service.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class StockReservedEventHandler : IEventHandler<StockReservedEvent>
{
    private readonly IPaymentStore _store;
    private readonly PaymentContext _context;
    private readonly IOutboxStore _outboxStore;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentMetrics _metrics;

    public StockReservedEventHandler(
        IPaymentStore store,
        PaymentContext context,
        IOutboxStore outboxStore,
        IPaymentGateway gateway,
        PaymentMetrics metrics)
    {
        _store = store;
        _context = context;
        _outboxStore = outboxStore;
        _gateway = gateway;
        _metrics = metrics;
    }

    public async Task Handle(StockReservedEvent @event)
    {
        var customerId = await _store.TryGetOrderCustomer(@event.OrderId);
        if (customerId is null)
        {
            // OrderCreatedEvent has not been observed yet for this order.
            // Mirrors Shipping's StockCommittedEventHandler — redelivery resolves the race.
            return;
        }

        var existing = await _store.GetByOrder(@event.OrderId);
        if (existing is not null)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        var result = await _gateway.AuthorizeAsync(
            @event.Amount, @event.Currency, @event.OrderId.ToString());
        _metrics.RecordAuthorizeLatency(sw.Elapsed);

        if (!result.Success)
        {
            // Phase 4 will land the failure path: payment.Fail() + PaymentFailedEvent.
            throw new InvalidOperationException(
                "Payment declined; failure-compensation path lands in Phase 4.");
        }

        await _outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var now = DateTime.UtcNow;
            var payment = Models.Payment.Create(
                paymentId: Guid.NewGuid(),
                orderId: @event.OrderId,
                customerId: customerId,
                amount: @event.Amount,
                currency: @event.Currency,
                createdAt: now);
            payment.Authorize(result.ProviderReference!, now);

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            await _outboxStore.AddOutboxEvent(new PaymentAuthorizedEvent(
                payment.PaymentId,
                payment.OrderId,
                payment.CustomerId,
                payment.Amount,
                payment.Currency));

            _metrics.RecordStatusChange(PaymentStatus.Authorized);

            scope.Complete();
        });
    }
}
