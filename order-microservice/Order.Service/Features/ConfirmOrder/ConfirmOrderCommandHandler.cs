using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Order.Service.Contracts.Integration;
using Order.Service.Domain;
using Order.Service.Infrastructure.Data.EntityFramework;

namespace Order.Service.Features.ConfirmOrder;

internal class ConfirmOrderCommandHandler : IEventHandler<ConfirmOrderCommand>
{
    private readonly OrderContext _context;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public ConfirmOrderCommandHandler(OrderContext context, IOutboxUnitOfWork outboxUnitOfWork)
    {
        _context = context;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task Handle(ConfirmOrderCommand command)
    {
        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.OrderId == command.OrderId);

            if (order is null)
            {
                return [];
            }

            if (order.Status == OrderStatus.Confirmed)
            {
                return [BuildReply(order, command)];
            }

            if (!order.TryConfirm())
            {
                return [];
            }

            // Saga path emits its own reply event; drain queued domain events so
            // OrderContext.ExecuteAsync translation cannot duplicate-publish.
            order.DequeueDomainEvents();

            await _context.SaveChangesAsync();

            return [BuildReply(order, command)];
        });
    }

    private static OrderConfirmedEvent BuildReply(Domain.Order order, ConfirmOrderCommand command) =>
        new(order.OrderId, order.CustomerId)
        {
            CorrelationId = command.CorrelationId,
            CausationId = command.Id,
            SagaId = command.SagaId,
        };
}
