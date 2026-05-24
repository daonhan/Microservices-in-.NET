using Microsoft.EntityFrameworkCore;
using Saga.Service.Domain;
using Saga.Service.Infrastructure.Data.EntityFramework;

namespace Saga.Service.Features.Operator.GetSaga;

internal sealed class GetSagaHandler
{
    private readonly SagaContext _sagaContext;

    public GetSagaHandler(SagaContext sagaContext)
    {
        _sagaContext = sagaContext;
    }

    public async Task<GetSagaResponse?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var saga = await _sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .Include(s => s.Transitions)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SagaId == id, cancellationToken);

        return saga is null ? null : Map(saga);
    }

    private static GetSagaResponse Map(SagaInstance saga) =>
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
            saga.OrderSagaState is null ? null : new GetSagaOrderStateResponse(
                saga.OrderSagaState.OrderId,
                saga.OrderSagaState.ReservationId,
                saga.OrderSagaState.PaymentId,
                saga.OrderSagaState.ShipmentId,
                saga.OrderSagaState.Amount,
                saga.OrderSagaState.CompensationOrigin,
                saga.OrderSagaState.LastStepResult),
            saga.Transitions
                .OrderBy(t => t.Timestamp)
                .ThenBy(t => t.Id)
                .Select(t => new GetSagaTransitionResponse(
                    t.Id,
                    t.FromStep,
                    t.ToStep,
                    t.Timestamp,
                    t.TriggerMessageId,
                    t.TriggerKind.ToString(),
                    t.Error))
                .ToArray());
}
