using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Saga.Service.Domain;
using Saga.Service.Domain.Abstractions;

namespace Saga.Service.Infrastructure.Data.EntityFramework;

internal sealed class EfSagaInstanceStore : ISagaInstanceStore
{
    private readonly SagaContext _ctx;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public EfSagaInstanceStore(SagaContext ctx, IOutboxUnitOfWork outboxUnitOfWork)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(outboxUnitOfWork);
        _ctx = ctx;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public Task<SagaInstance?> GetOrderSagaBySagaId(Guid sagaId, CancellationToken cancellationToken = default) =>
        _ctx.SagaInstances
            .Include(s => s.OrderSagaState)
            .FirstOrDefaultAsync(s => s.SagaId == sagaId, cancellationToken);

    public Task<SagaInstance?> GetOrderSagaByOrderId(Guid orderId, CancellationToken cancellationToken = default) =>
        _ctx.SagaInstances
            .Include(s => s.OrderSagaState)
            .FirstOrDefaultAsync(s => s.OrderSagaState!.OrderId == orderId, cancellationToken);

    public Task<SagaInstance?> GetRefundSagaBySagaId(Guid sagaId, CancellationToken cancellationToken = default) =>
        _ctx.SagaInstances
            .Include(s => s.RefundSagaState)
            .FirstOrDefaultAsync(s => s.SagaId == sagaId, cancellationToken);

    public Task<SagaInstance?> GetRefundSagaByOrderId(Guid orderId, CancellationToken cancellationToken = default) =>
        _ctx.SagaInstances
            .Include(s => s.RefundSagaState)
            .FirstOrDefaultAsync(s => s.RefundSagaState!.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<SagaInstance>> GetOverdueOrderSagas(string sagaType, DateTime now, CancellationToken cancellationToken = default) =>
        await _ctx.SagaInstances
            .Include(s => s.OrderSagaState)
            .Where(s => s.SagaType == sagaType
                && s.Status == SagaStatus.Running
                && s.NextTimeoutAt != null
                && s.NextTimeoutAt <= now)
            .OrderBy(s => s.NextTimeoutAt)
            .ToListAsync(cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _ctx.SaveChangesAsync(cancellationToken);

    public Task ExecuteAsync(Func<Task<IReadOnlyList<Event>>> work) =>
        _outboxUnitOfWork.ExecuteAsync(_ctx.Database.CreateExecutionStrategy(), work);
}
