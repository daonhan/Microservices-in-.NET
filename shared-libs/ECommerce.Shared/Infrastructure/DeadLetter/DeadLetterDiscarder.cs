using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.Extensions.Logging;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public sealed partial class DeadLetterDiscarder : IDeadLetterDiscarder
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Discarded dead-letter message {FailureId} (event={EventType}, queue={OriginalQueue}, service={Service}) on behalf of {DiscardedBy}: {Reason}")]
    private static partial void LogDiscardSuccess(ILogger logger, Guid failureId, string eventType, string originalQueue, string service, string discardedBy, string reason);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Refused to discard dead-letter message {FailureId}: {Reason} (requested by {DiscardedBy}).")]
    private static partial void LogDiscardRefused(ILogger logger, Guid failureId, string reason, string discardedBy);

    private readonly IDeadLetterStore _store;
    private readonly ILogger<DeadLetterDiscarder> _logger;

    public DeadLetterDiscarder(IDeadLetterStore store, ILogger<DeadLetterDiscarder> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<DeadLetterDiscardResult> DiscardAsync(Guid failureId, string discardedBy, string discardReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(discardReason))
        {
            LogDiscardRefused(_logger, failureId, "reason_required", discardedBy);
            return new DeadLetterDiscardResult(DeadLetterDiscardOutcome.ReasonRequired, "reason_required", null);
        }

        if (string.IsNullOrWhiteSpace(discardedBy))
        {
            discardedBy = "unknown";
        }

        using var activity = DeadLetterMetrics.ActivitySource.StartActivity(
            "dlq.discard", System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag("dlq.failure_id", failureId.ToString());
        activity?.SetTag("dlq.discarded_by", discardedBy);

        var message = await _store.GetAsync(failureId, cancellationToken);
        if (message is null)
        {
            LogDiscardRefused(_logger, failureId, "not_found", discardedBy);
            activity?.SetTag("dlq.outcome", "not_found");
            return new DeadLetterDiscardResult(DeadLetterDiscardOutcome.NotFound, "not_found", null);
        }

        activity?.SetTag("dlq.event_type", message.EventType);
        activity?.SetTag("dlq.original_queue", message.OriginalQueue);
        activity?.SetTag("dlq.service", message.Service);
        if (message.CorrelationId is { } correlationId)
        {
            activity?.SetTag("messaging.correlation_id", correlationId.ToString());
        }

        if (message.Status != DeadLetterStatus.Pending)
        {
            LogDiscardRefused(_logger, failureId, $"status_{message.Status}", discardedBy);
            RecordOutcome(message, outcome: "failure");
            activity?.SetTag("dlq.outcome", $"not_pending_{message.Status}");
            return new DeadLetterDiscardResult(DeadLetterDiscardOutcome.NotPending, $"status_{message.Status}", message);
        }

        var marked = await _store.MarkDiscardedAsync(failureId, discardedBy, discardReason, cancellationToken);
        if (!marked)
        {
            LogDiscardRefused(_logger, failureId, "concurrent_transition", discardedBy);
            RecordOutcome(message, outcome: "failure");
            activity?.SetTag("dlq.outcome", "concurrent_transition");
            return new DeadLetterDiscardResult(DeadLetterDiscardOutcome.NotPending, "concurrent_transition", message);
        }

        LogDiscardSuccess(_logger, message.Id, message.EventType, message.OriginalQueue, message.Service, discardedBy, discardReason);
        RecordOutcome(message, outcome: "success");
        activity?.SetTag("dlq.outcome", "success");
        return new DeadLetterDiscardResult(DeadLetterDiscardOutcome.Success, null, message);
    }

    private static void RecordOutcome(DeadLetterMessage message, string outcome)
    {
        DeadLetterMetrics.Discards.Add(1,
            new KeyValuePair<string, object?>("service", message.Service),
            new KeyValuePair<string, object?>("event_type", message.EventType),
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}
