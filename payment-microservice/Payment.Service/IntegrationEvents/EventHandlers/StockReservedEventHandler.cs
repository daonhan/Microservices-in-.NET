using System.Diagnostics;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Payment.Service.Infrastructure.Data;
using Payment.Service.Infrastructure.Gateways;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;
using Payment.Service.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class StockReservedEventHandler : IEventHandler<StockReservedEvent>
{
    private readonly IPaymentStore _store;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentMetrics _metrics;

    public StockReservedEventHandler(
        IPaymentStore store,
        IPaymentGateway gateway,
        PaymentMetrics metrics)
    {
        _store = store;
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

        var now = DateTime.UtcNow;
        var payment = Models.Payment.Create(
            paymentId: Guid.NewGuid(),
            orderId: @event.OrderId,
            customerId: customerId,
            amount: @event.Amount,
            currency: @event.Currency,
            createdAt: now);

        await _store.ExecuteAsync(async () =>
        {
            await _store.Add(payment);

            if (result.Success)
            {
                payment.Authorize(result.ProviderReference!, now);
            }
            else
            {
                payment.Fail(result.FailureReason ?? "Declined", now);
            }
        });

        _metrics.RecordStatusChange(
            result.Success ? PaymentStatus.Authorized : PaymentStatus.Failed);
    }
}
