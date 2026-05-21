using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Order.Service.Contracts.Integration;
using Order.Service.Domain;
using Order.Service.Infrastructure.Data.EntityFramework;

namespace Order.Service.IntegrationEvents.EventHandlers;

internal class CancelOrderCommandHandler : IEventHandler<CancelOrderCommand>
{
    private readonly OrderContext _context;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public CancelOrderCommandHandler(OrderContext context, IOutboxUnitOfWork outboxUnitOfWork)
    {
        _context = context;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task Handle(CancelOrderCommand command)
    {
        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.OrderId == command.OrderId);

            if (order is null)
            {
                return [];
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                return [BuildReply(order, command)];
            }

            if (!order.TryCancel())
            {
                return [];
            }

            order.DequeueDomainEvents();

            await _context.SaveChangesAsync();

            return [BuildReply(order, command)];
        });
    }

    private static OrderCancelledEvent BuildReply(Domain.Order order, CancelOrderCommand command) =>
        new(order.OrderId, order.CustomerId)
        {
            CorrelationId = command.CorrelationId,
            CausationId = command.Id,
            SagaId = command.SagaId,
        };
}
