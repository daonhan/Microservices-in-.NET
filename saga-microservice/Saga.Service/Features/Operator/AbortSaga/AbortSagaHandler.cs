using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Domain;
using Saga.Service.Domain.Abstractions;

namespace Saga.Service.Features.Operator.AbortSaga;

internal sealed class AbortSagaHandler
{
    private const string SagaPathPrefix = "/operator/api/sagas";

    private readonly IOrderSagaTransitionRunner _runner;

    public AbortSagaHandler(IOrderSagaTransitionRunner runner)
    {
        _runner = runner;
    }

    public async Task<IResult> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var outcome = await _runner.BeginCompensation(
            id,
            new Event { SagaId = id },
            SagaTriggerKind.OperatorAction,
            "Operator abort started saga compensation.",
            cancellationToken);

        return outcome.Status switch
        {
            SagaCompensationOutcomeStatus.Applied => Results.Accepted(
                $"{SagaPathPrefix}/{outcome.SagaId}",
                new AbortSagaResponse(
                    outcome.SagaId,
                    outcome.CurrentStatus!,
                    outcome.CurrentStep!,
                    outcome.CommandId)),
            SagaCompensationOutcomeStatus.NotFound => TypedResults.NotFound(),
            _ => Results.Conflict(new { id, reason = outcome.Reason ?? "compensation_not_started" })
        };
    }
}
