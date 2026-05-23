using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class CapturePaymentCommandHandler : IEventHandler<CapturePaymentCommand>
{
    private readonly IPaymentStore _store;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly PaymentMetrics _metrics;

    public CapturePaymentCommandHandler(
        IPaymentStore store,
        IOutboxUnitOfWork outboxUnitOfWork,
        PaymentMetrics metrics)
    {
        _store = store;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metrics = metrics;
    }

    public async Task Handle(CapturePaymentCommand command)
    {
        var payment = await _store.GetByOrder(command.OrderId);
        if (payment is null)
        {
            return;
        }

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            if (payment.Status == PaymentStatus.Captured)
            {
                return [BuildCapturedReply(payment, command)];
            }

            if (payment.Status != PaymentStatus.Authorized)
            {
                return [];
            }

            payment.Capture(DateTime.UtcNow);
            payment.DequeueDomainEvents();

            await _store.SaveChangesAsync();
            _metrics.RecordStatusChange(payment.Status);

            return [BuildCapturedReply(payment, command)];
        });
    }

    private static PaymentCapturedEvent BuildCapturedReply(Domain.Payment payment, CapturePaymentCommand command) =>
        new(payment.PaymentId, payment.OrderId, payment.Amount)
        {
            CorrelationId = command.CorrelationId,
            CausationId = command.Id,
            SagaId = command.SagaId,
        };
}
