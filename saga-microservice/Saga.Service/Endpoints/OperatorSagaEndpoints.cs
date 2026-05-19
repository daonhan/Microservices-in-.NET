using ECommerce.Shared.Authentication;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Saga.Service.ApiModels;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Reaper;
using Saga.Service.Models;
using Saga.Service.Observability;
using Saga.Service.StateMachines;

namespace Saga.Service.Endpoints;

public static class OperatorSagaEndpoints
{
    private const string OperatorSagaPath = "/operator/api/sagas";

    public static void RegisterOperatorSagaEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup(OperatorSagaPath)
            .RequireAuthorization(AuthorizationPolicies.RequireServicePolicy)
            .WithTags("Operator Sagas");

        group.MapGet("", ListSagas)
            .WithName("ListOperatorSagas")
            .WithSummary("List saga instances for operator workflows.")
            .Produces<SagaListResponse>();

        group.MapGet("/{id:guid}", GetSagaDetail)
            .WithName("GetOperatorSagaDetail")
            .WithSummary("Get saga detail and transition history.")
            .Produces<SagaDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/retry", RetrySaga)
            .WithName("RetryOperatorSaga")
            .WithSummary("Requeue the in-flight command for a saga.")
            .Produces<OperatorSagaActionResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/abort", AbortSaga)
            .WithName("AbortOperatorSaga")
            .WithSummary("Force a running saga into compensation.")
            .Produces<OperatorSagaActionResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListSagas(
        SagaContext sagaContext,
        TimeProvider timeProvider,
        string? type,
        SagaStatus? status,
        bool? overdue,
        CancellationToken cancellationToken)
    {
        var query = sagaContext.SagaInstances
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
            var now = timeProvider.GetUtcNow().UtcDateTime;
            query = query.Where(s => s.Status == SagaStatus.Running
                && s.NextTimeoutAt != null
                && s.NextTimeoutAt <= now);
        }

        var sagas = await query
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new SagaListResponse(
            sagas.Select(MapListItem).ToArray(),
            sagas.Count));
    }

    private static async Task<IResult> GetSagaDetail(
        Guid id,
        SagaContext sagaContext,
        CancellationToken cancellationToken)
    {
        var saga = await sagaContext.SagaInstances
            .Include(s => s.OrderSagaState)
            .Include(s => s.Transitions)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SagaId == id, cancellationToken);

        return saga is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(MapDetail(saga));
    }

    private static async Task<IResult> RetrySaga(
        Guid id,
        SagaContext sagaContext,
        IOutboxStore outboxStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        OrderSagaTimeoutScheduler timeoutScheduler,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        IResult result = TypedResults.NotFound();

        await outboxUnitOfWork.ExecuteAsync(sagaContext.Database.CreateExecutionStrategy(), async () =>
        {
            var saga = await sagaContext.SagaInstances
                .FirstOrDefaultAsync(s => s.SagaId == id, cancellationToken);

            if (saga is null)
            {
                return [];
            }

            if (saga.Status is not (SagaStatus.Running or SagaStatus.Compensating))
            {
                result = Results.Conflict(new { id, reason = $"status_{saga.Status}" });
                return [];
            }

            if (saga.LastCommandId is not { } commandId)
            {
                result = Results.Conflict(new { id, reason = "no_in_flight_command" });
                return [];
            }

            if (!await outboxStore.RequeueOutboxEvent(commandId))
            {
                result = Results.Conflict(new { id, reason = "in_flight_command_not_found" });
                return [];
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            saga.RetryCount = 0;
            saga.UpdatedAt = now;
            timeoutScheduler.MoveForward(saga, now);
            saga.Transitions.Add(new SagaTransition
            {
                SagaId = saga.SagaId,
                FromStep = saga.CurrentStep,
                ToStep = saga.CurrentStep,
                Timestamp = now,
                TriggerMessageId = commandId,
                TriggerKind = SagaTriggerKind.OperatorAction,
                Error = "Operator retry requeued the in-flight command."
            });

            await sagaContext.SaveChangesAsync(cancellationToken);

            result = Results.Accepted(
                $"{OperatorSagaPath}/{saga.SagaId}",
                new OperatorSagaActionResponse(
                    saga.SagaId,
                    saga.Status.ToString(),
                    saga.CurrentStep,
                    saga.LastCommandId));
            return [];
        });

        return result;
    }

    private static async Task<IResult> AbortSaga(
        Guid id,
        SagaContext sagaContext,
        IOutboxUnitOfWork outboxUnitOfWork,
        OrderSagaTimeoutScheduler timeoutScheduler,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        IResult result = TypedResults.NotFound();

        await outboxUnitOfWork.ExecuteAsync(sagaContext.Database.CreateExecutionStrategy(), async () =>
        {
            var saga = await sagaContext.SagaInstances
                .Include(s => s.OrderSagaState)
                .FirstOrDefaultAsync(s => s.SagaId == id, cancellationToken);

            if (saga is null)
            {
                return [];
            }

            if (saga.OrderSagaState is null)
            {
                result = Results.Conflict(new { id, reason = "unsupported_saga_type" });
                return [];
            }

            if (saga.Status != SagaStatus.Running)
            {
                result = Results.Conflict(new { id, reason = $"status_{saga.Status}" });
                return [];
            }

            if (!Enum.TryParse<OrderSagaStep>(saga.CurrentStep, out var currentStep))
            {
                result = Results.Conflict(new { id, reason = "unknown_current_step" });
                return [];
            }

            var trigger = new Event
            {
                CorrelationId = saga.CorrelationId,
                SagaId = saga.SagaId
            };
            var snapshot = new OrderSagaStateSnapshot(
                saga.SagaId,
                saga.OrderSagaState.OrderId,
                currentStep,
                saga.Status,
                saga.OrderSagaState.LastStepResult,
                saga.OrderSagaState.Amount,
                ParseStep(saga.OrderSagaState.CompensationOrigin));
            var origin = OrderSagaStateMachine.GetLastCompletedStep(currentStep);
            var transition = OrderSagaStateMachine.BeginCompensation(snapshot, origin, trigger);

            if (!transition.Changed)
            {
                result = Results.Conflict(new { id, reason = "compensation_not_started" });
                return [];
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var previousStatus = saga.Status;
            saga.CurrentStep = transition.State.CurrentStep.ToString();
            saga.Status = transition.State.Status;
            saga.UpdatedAt = now;
            saga.OrderSagaState.LastStepResult = transition.State.LastStepResult;
            saga.OrderSagaState.Amount = transition.State.Amount;
            saga.OrderSagaState.CompensationOrigin = transition.State.CompensationOrigin?.ToString();
            saga.LastCommandId = transition.Commands.Count == 0 ? null : transition.Commands[0].Id;
            timeoutScheduler.Apply(saga, now);
            saga.Transitions.Add(new SagaTransition
            {
                SagaId = saga.SagaId,
                FromStep = currentStep.ToString(),
                ToStep = transition.State.CurrentStep.ToString(),
                Timestamp = now,
                TriggerMessageId = trigger.Id,
                TriggerKind = SagaTriggerKind.OperatorAction,
                Error = "Operator abort started saga compensation."
            });

            await sagaContext.SaveChangesAsync(cancellationToken);

            if (previousStatus == SagaStatus.Running && saga.Status == SagaStatus.Compensating)
            {
                SagaTelemetry.Compensation.Add(1, new KeyValuePair<string, object?>("type", saga.SagaType));
            }

            result = Results.Accepted(
                $"{OperatorSagaPath}/{saga.SagaId}",
                new OperatorSagaActionResponse(
                    saga.SagaId,
                    saga.Status.ToString(),
                    saga.CurrentStep,
                    saga.LastCommandId));
            return transition.Commands;
        });

        return result;
    }

    private static SagaListItemResponse MapListItem(SagaInstance saga) =>
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

    private static SagaDetailResponse MapDetail(SagaInstance saga) =>
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
            saga.OrderSagaState is null ? null : new OrderSagaStateResponse(
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
                .Select(t => new SagaTransitionResponse(
                    t.Id,
                    t.FromStep,
                    t.ToStep,
                    t.Timestamp,
                    t.TriggerMessageId,
                    t.TriggerKind.ToString(),
                    t.Error))
                .ToArray());

    private static OrderSagaStep? ParseStep(string? value) =>
        Enum.TryParse<OrderSagaStep>(value, out var parsed) ? parsed : null;
}
