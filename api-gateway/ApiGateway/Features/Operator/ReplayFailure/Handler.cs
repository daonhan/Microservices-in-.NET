using System.Security.Claims;
using ApiGateway.Infrastructure.Auth;
using ECommerce.Shared.Infrastructure.DeadLetter;

namespace ApiGateway.Features.Operator.ReplayFailure;

internal sealed class ReplayFailureHandler
{
    private readonly IDeadLetterReplayer _replayer;

    public ReplayFailureHandler(IDeadLetterReplayer replayer)
    {
        _replayer = replayer;
    }

    public async Task<IResult> HandleAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var replayedBy = user.FindFirstValue(JwtClaimTypes.Subject)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.Identity?.Name
            ?? "unknown";

        var result = await _replayer.ReplayAsync(id, replayedBy, cancellationToken);

        return result.Outcome switch
        {
            DeadLetterReplayOutcome.Success =>
                Results.Accepted($"/operator/api/failures/{id}", new { id, newMessageId = result.NewMessageId }),
            DeadLetterReplayOutcome.NotFound =>
                Results.NotFound(new { id, reason = result.FailureReason }),
            DeadLetterReplayOutcome.NotPending =>
                Results.Conflict(new { id, reason = result.FailureReason }),
            _ =>
                Results.Problem(
                    title: "Replay failed during publish",
                    detail: result.FailureReason,
                    statusCode: StatusCodes.Status502BadGateway)
        };
    }
}
