using ECommerce.Shared.Infrastructure.DeadLetter.Models;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public interface IDeadLetterReplayer
{
    Task<DeadLetterReplayResult> ReplayAsync(Guid failureId, string replayedBy, CancellationToken cancellationToken = default);
}

public enum DeadLetterReplayOutcome
{
    Success = 0,
    NotFound = 1,
    NotPending = 2,
    PublishFailed = 3
}

public sealed record DeadLetterReplayResult(
    DeadLetterReplayOutcome Outcome,
    Guid? NewMessageId,
    string? FailureReason,
    DeadLetterMessage? Message);
