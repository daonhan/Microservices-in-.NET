using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Domain.Abstractions;

internal interface ISagaInstanceStore
{
    Task<SagaInstance?> GetOrderSagaBySagaId(Guid sagaId, CancellationToken cancellationToken = default);

    Task<SagaInstance?> GetOrderSagaByOrderId(Guid orderId, CancellationToken cancellationToken = default);

    Task<SagaInstance?> GetRefundSagaBySagaId(Guid sagaId, CancellationToken cancellationToken = default);

    Task<SagaInstance?> GetRefundSagaByOrderId(Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SagaInstance>> GetOverdueOrderSagas(string sagaType, DateTime now, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task ExecuteAsync(Func<Task<IReadOnlyList<Event>>> work);
}
