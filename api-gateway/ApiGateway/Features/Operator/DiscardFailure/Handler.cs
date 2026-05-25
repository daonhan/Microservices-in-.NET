using System.Security.Claims;
using ApiGateway.Infrastructure.Auth;
using ECommerce.Shared.Infrastructure.DeadLetter;

namespace ApiGateway.Features.Operator.DiscardFailure;

internal sealed class DiscardFailureHandler
{
    private readonly IDeadLetterDiscarder _discarder;

    public DiscardFailureHandler(IDeadLetterDiscarder discarder)
    {
        _discarder = discarder;
    }

    public async Task<IResult> HandleAsync(
        Guid id,
        DiscardRequest? request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var reason = request?.Reason;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Results.BadRequest(new { id, error = "discard reason is required" });
        }

        var discardedBy = user.FindFirstValue(JwtClaimTypes.Subject)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.Identity?.Name
            ?? "unknown";

        var result = await _discarder.DiscardAsync(id, discardedBy, reason, cancellationToken);

        return result.Outcome switch
        {
            DeadLetterDiscardOutcome.Success =>
                Results.Accepted($"/operator/api/failures/{id}", new { id, discardedBy, reason }),
            DeadLetterDiscardOutcome.NotFound =>
                Results.NotFound(new { id, reason = result.FailureReason }),
            DeadLetterDiscardOutcome.NotPending =>
                Results.Conflict(new { id, reason = result.FailureReason }),
            DeadLetterDiscardOutcome.ReasonRequired =>
                Results.BadRequest(new { id, error = "discard reason is required" }),
            _ =>
                Results.Problem(
                    title: "Discard failed",
                    detail: result.FailureReason,
                    statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
