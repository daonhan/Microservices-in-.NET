using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Infrastructure.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class RefundPaymentCommandHandler : IEventHandler<RefundPaymentCommand>
{
    private readonly IPaymentStore _store;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly PaymentMetrics _metrics;

    public RefundPaymentCommandHandler(
        IPaymentStore store,
        IOutboxUnitOfWork outboxUnitOfWork,
        PaymentMetrics metrics)
    {
        _store = store;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metrics = metrics;
    }

    public async Task Handle(RefundPaymentCommand command)
    {
        var payment = await _store.GetByOrder(command.OrderId);
        if (payment is null)
        {
            return;
        }

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.Refunded))
            {
                return [];
            }

            if (payment.Status != PaymentStatus.Refunded)
            {
                payment.Refund(command.Amount, DateTime.UtcNow);
                payment.DequeueDomainEvents();
                await _store.SaveChangesAsync();
                _metrics.RecordStatusChange(payment.Status);
            }

            return [BuildReply(payment, command)];
        });
    }

    private static PaymentRefundedEvent BuildReply(Domain.Payment payment, RefundPaymentCommand command) =>
        new(payment.PaymentId, payment.OrderId, command.Amount)
        {
            CorrelationId = command.CorrelationId,
            CausationId = command.Id,
            SagaId = command.SagaId,
        };
}
