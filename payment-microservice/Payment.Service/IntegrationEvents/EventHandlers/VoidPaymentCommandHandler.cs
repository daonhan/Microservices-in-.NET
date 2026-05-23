using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Infrastructure.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class VoidPaymentCommandHandler : IEventHandler<VoidPaymentCommand>
{
    private readonly IPaymentStore _store;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly PaymentMetrics _metrics;

    public VoidPaymentCommandHandler(
        IPaymentStore store,
        IOutboxUnitOfWork outboxUnitOfWork,
        PaymentMetrics metrics)
    {
        _store = store;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metrics = metrics;
    }

    public async Task Handle(VoidPaymentCommand command)
    {
        var payment = await _store.GetByOrder(command.OrderId);
        if (payment is null)
        {
            return;
        }

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.Voided))
            {
                return [];
            }

            if (payment.Status != PaymentStatus.Voided)
            {
                payment.Void(command.Reason, DateTime.UtcNow);
                payment.DequeueDomainEvents();
                await _store.SaveChangesAsync();
                _metrics.RecordStatusChange(payment.Status);
            }

            return [BuildReply(payment, command)];
        });
    }

    private static PaymentVoidedEvent BuildReply(Domain.Payment payment, VoidPaymentCommand command) =>
        new(payment.PaymentId, payment.OrderId, payment.CustomerId, command.Reason)
        {
            CorrelationId = command.CorrelationId,
            CausationId = command.Id,
            SagaId = command.SagaId,
        };
}
