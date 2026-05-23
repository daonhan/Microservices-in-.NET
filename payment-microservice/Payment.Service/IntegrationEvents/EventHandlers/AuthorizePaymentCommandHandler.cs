using System.Diagnostics;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Infrastructure.Observability;
using Payment.Service.Infrastructure.Outbox;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class AuthorizePaymentCommandHandler : IEventHandler<AuthorizePaymentCommand>
{
    private readonly IPaymentStore _store;
    private readonly IPaymentGateway _gateway;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly MessageCorrelationContext _correlation;
    private readonly PaymentMetrics _metrics;

    public AuthorizePaymentCommandHandler(
        IPaymentStore store,
        IPaymentGateway gateway,
        IOutboxUnitOfWork outboxUnitOfWork,
        MessageCorrelationContext correlation,
        PaymentMetrics metrics)
    {
        _store = store;
        _gateway = gateway;
        _outboxUnitOfWork = outboxUnitOfWork;
        _correlation = correlation;
        _metrics = metrics;
    }

    public async Task Handle(AuthorizePaymentCommand command)
    {
        var existing = await _store.GetByOrder(command.OrderId);
        if (existing is not null)
        {
            await _outboxUnitOfWork.ExecuteAsync(() =>
                Task.FromResult<IReadOnlyList<Event>>([BuildIdempotentReply(existing, command)]));
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

        _correlation.CorrelationId = command.CorrelationId;
        _correlation.CausationId = command.Id;
        _correlation.SagaId = command.SagaId;

        await _store.ExecuteAsync(() =>
        {
            _store.Add(payment);

            if (result.Success)
            {
                payment.Authorize(result.ProviderReference!, now);
            }
            else
            {
                payment.Fail(result.FailureReason ?? "Declined", now);
            }

            return Task.CompletedTask;
        });

        _metrics.RecordStatusChange(payment.Status);
    }

    private static Event BuildIdempotentReply(Domain.Payment payment, AuthorizePaymentCommand command)
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
