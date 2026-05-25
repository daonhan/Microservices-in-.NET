using System.Security.Claims;
using ApiGateway.Infrastructure.Auth;
using ECommerce.Shared.Infrastructure.DeadLetter;

namespace ApiGateway.Features.Operator.BatchReplayFailures;

internal sealed class BatchReplayFailuresHandler
{
    private readonly IDeadLetterReplayer _replayer;

    public BatchReplayFailuresHandler(IDeadLetterReplayer replayer)
    {
        _replayer = replayer;
    }

    public async Task<IResult> HandleAsync(
        BatchReplayRequest? request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Ids is null || request.Ids.Count == 0)
        {
            return Results.BadRequest(new { error = "ids are required" });
        }

        var replayedBy = user.FindFirstValue(JwtClaimTypes.Subject)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.Identity?.Name
            ?? "unknown";

        var items = new List<BatchReplayItem>(request.Ids.Count);
        foreach (var id in request.Ids)
        {
            var result = await _replayer.ReplayAsync(id, replayedBy, cancellationToken);
            var status = result.Outcome switch
            {
                DeadLetterReplayOutcome.Success => "success",
                DeadLetterReplayOutcome.NotFound => "not_found",
                DeadLetterReplayOutcome.NotPending => "not_pending",
                DeadLetterReplayOutcome.PublishFailed => "publish_failed",
                _ => "unknown"
            };
            items.Add(new BatchReplayItem(id, status, result.NewMessageId, result.FailureReason));
        }

        return Results.Ok(new BatchReplayResponse(items));
    }
}
