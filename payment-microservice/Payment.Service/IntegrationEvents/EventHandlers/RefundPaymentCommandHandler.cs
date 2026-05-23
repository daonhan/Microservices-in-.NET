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

internal class RefundPaymentCommandHandler : IEventHandler<RefundPaymentCommand>
{
    private readonly IPaymentStore _store;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly MessageCorrelationContext _correlation;
    private readonly PaymentMetrics _metrics;

    public RefundPaymentCommandHandler(
        IPaymentStore store,
        IOutboxUnitOfWork outboxUnitOfWork,
        MessageCorrelationContext correlation,
        PaymentMetrics metrics)
    {
        _store = store;
        _outboxUnitOfWork = outboxUnitOfWork;
        _correlation = correlation;
        _metrics = metrics;
    }

    public async Task Handle(RefundPaymentCommand command)
    {
        var payment = await _store.GetByOrder(command.OrderId);
        if (payment is null)
        {
            return;
        }

        if (payment.Status == PaymentStatus.Refunded)
        {
            await _outboxUnitOfWork.ExecuteAsync(() =>
                Task.FromResult<IReadOnlyList<Event>>([BuildIdempotentReply(payment, command)]));
            return;
        }

        if (payment.Status != PaymentStatus.Captured)
        {
            return;
        }

        _correlation.CorrelationId = command.CorrelationId;
        _correlation.CausationId = command.Id;
        _correlation.SagaId = command.SagaId;

        await _store.ExecuteAsync(() =>
        {
            payment.Refund(command.Amount, DateTime.UtcNow);
            return Task.CompletedTask;
        });

        _metrics.RecordStatusChange(payment.Status);
    }

    private static PaymentRefundedEvent BuildIdempotentReply(Domain.Payment payment, RefundPaymentCommand command) =>
        new(payment.PaymentId, payment.OrderId, command.Amount)
        {
            CorrelationId = command.CorrelationId,
            CausationId = command.Id,
            SagaId = command.SagaId,
        };
}
