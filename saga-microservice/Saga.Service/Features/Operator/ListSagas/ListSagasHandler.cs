using Microsoft.EntityFrameworkCore;
using Saga.Service.Domain;
using Saga.Service.Infrastructure.Data.EntityFramework;

namespace Saga.Service.Features.Operator.ListSagas;

internal sealed class ListSagasHandler
{
    private readonly SagaContext _sagaContext;
    private readonly TimeProvider _timeProvider;

    public ListSagasHandler(SagaContext sagaContext, TimeProvider timeProvider)
    {
        _sagaContext = sagaContext;
        _timeProvider = timeProvider;
    }

    public async Task<ListSagasResponse> HandleAsync(
        string? type,
        SagaStatus? status,
        bool? overdue,
        CancellationToken cancellationToken)
    {
        var query = _sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(s => s.SagaType == type);
        }

        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        if (overdue == true)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            query = query.Where(s => s.Status == SagaStatus.Running
                && s.NextTimeoutAt != null
                && s.NextTimeoutAt <= now);
        }

        var sagas = await query
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);

        return new ListSagasResponse(
            sagas.Select(MapItem).ToArray(),
            sagas.Count);
    }

    private static ListSagasItemResponse MapItem(SagaInstance saga) =>
        new(
            saga.SagaId,
            saga.SagaType,
            saga.CurrentStep,
            saga.Status.ToString(),
            saga.CorrelationId,
            saga.CreatedAt,
            saga.UpdatedAt,
            saga.NextTimeoutAt,
            saga.RetryCount,
            saga.LastCommandId,
            saga.OrderSagaState?.OrderId);
}
