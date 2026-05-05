using ECommerce.Shared.Infrastructure.DeadLetter.Models;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public interface IDeadLetterDiscarder
{
    Task<DeadLetterDiscardResult> DiscardAsync(Guid failureId, string discardedBy, string discardReason, CancellationToken cancellationToken = default);
}

public enum DeadLetterDiscardOutcome
{
    Success = 0,
    NotFound = 1,
    NotPending = 2,
    ReasonRequired = 3
}

public sealed record DeadLetterDiscardResult(
    DeadLetterDiscardOutcome Outcome,
    string? FailureReason,
    DeadLetterMessage? Message);
