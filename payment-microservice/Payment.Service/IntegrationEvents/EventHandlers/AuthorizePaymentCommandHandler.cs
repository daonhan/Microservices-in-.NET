using System.Diagnostics;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class AuthorizePaymentCommandHandler : IEventHandler<AuthorizePaymentCommand>
{
    private readonly IPaymentStore _store;
    private readonly IPaymentGateway _gateway;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly PaymentMetrics _metrics;

    public AuthorizePaymentCommandHandler(
        IPaymentStore store,
        IPaymentGateway gateway,
        IOutboxUnitOfWork outboxUnitOfWork,
        PaymentMetrics metrics)
    {
        _store = store;
        _gateway = gateway;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metrics = metrics;
    }

    public async Task Handle(AuthorizePaymentCommand command)
    {
        var existing = await _store.GetByOrder(command.OrderId);
        if (existing is not null)
        {
            await _outboxUnitOfWork.ExecuteAsync(() =>
                Task.FromResult<IReadOnlyList<Event>>([BuildReply(existing, command)]));
            return;
        }

        var customerId = await _store.TryGetOrderCustomer(command.OrderId);
        if (customerId is null)
        {
            // OrderCreatedEvent has not been observed yet for this order — redelivery resolves the race.
            return;
        }

        var sw = Stopwatch.StartNew();
        var result = await _gateway.AuthorizeAsync(
            command.Amount, command.Currency, command.OrderId.ToString());
        _metrics.RecordAuthorizeLatency(sw.Elapsed);

        var now = DateTime.UtcNow;
        var payment = Domain.Payment.Create(
            paymentId: Guid.NewGuid(),
            orderId: command.OrderId,
            customerId: customerId,
            amount: command.Amount,
            currency: command.Currency,
            createdAt: now);

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            _store.Add(payment);

            Event reply;
            if (result.Success)
            {
                payment.Authorize(result.ProviderReference!, now);
                reply = new PaymentAuthorizedEvent(
                    payment.PaymentId,
                    payment.OrderId,
                    payment.CustomerId,
                    payment.Amount,
                    payment.Currency)
                {
                    CorrelationId = command.CorrelationId,
                    CausationId = command.Id,
                    SagaId = command.SagaId,
                };
            }
            else
            {
                var reason = result.FailureReason ?? "Declined";
                payment.Fail(reason, now);
                reply = new PaymentFailedEvent(
                    payment.PaymentId,
                    payment.OrderId,
                    payment.CustomerId,
                    reason)
                {
                    CorrelationId = command.CorrelationId,
                    CausationId = command.Id,
                    SagaId = command.SagaId,
                };
            }

            // Drain queued domain events — saga path emits the reply explicitly so
            // the PaymentContext translation must not fire and duplicate-publish.
            payment.DequeueDomainEvents();

            await _store.SaveChangesAsync();
            _metrics.RecordStatusChange(payment.Status);

            return [reply];
        });
    }

    private static Event BuildReply(Domain.Payment payment, AuthorizePaymentCommand command)
    {
        return payment.Status switch
        {
            PaymentStatus.Authorized
                or PaymentStatus.Captured
                or PaymentStatus.Refunded => new PaymentAuthorizedEvent(
                    payment.PaymentId,
                    payment.OrderId,
                    payment.CustomerId,
                    payment.Amount,
                    payment.Currency)
                {
                    CorrelationId = command.CorrelationId,
                    CausationId = command.Id,
                    SagaId = command.SagaId,
                },
            PaymentStatus.Failed
                or PaymentStatus.Voided => new PaymentFailedEvent(
                    payment.PaymentId,
                    payment.OrderId,
                    payment.CustomerId,
                    payment.Status.ToString())
                {
                    CorrelationId = command.CorrelationId,
                    CausationId = command.Id,
                    SagaId = command.SagaId,
                },
            _ => throw new InvalidOperationException(
                $"Cannot emit AuthorizePayment reply for payment {payment.PaymentId} in status {payment.Status}.")
        };
    }
}
