using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.Extensions.Logging;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public sealed partial class DeadLetterReplayer : IDeadLetterReplayer
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Replayed dead-letter message {FailureId} (event={EventType}, queue={OriginalQueue}, service={Service}) as {NewMessageId} on behalf of {ReplayedBy}.")]
    private static partial void LogReplaySuccess(ILogger logger, Guid failureId, string eventType, string originalQueue, string service, Guid newMessageId, string replayedBy);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Refused to replay dead-letter message {FailureId}: {Reason} (requested by {ReplayedBy}).")]
    private static partial void LogReplayRefused(ILogger logger, Guid failureId, string reason, string replayedBy);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "Replay of dead-letter message {FailureId} failed during publish (requested by {ReplayedBy}).")]
    private static partial void LogReplayPublishFailed(ILogger logger, Exception ex, Guid failureId, string replayedBy);

    private readonly IDeadLetterStore _store;
    private readonly IDeadLetterPublisher _publisher;
    private readonly ILogger<DeadLetterReplayer> _logger;

    public DeadLetterReplayer(
        IDeadLetterStore store,
        IDeadLetterPublisher publisher,
        ILogger<DeadLetterReplayer> logger)
    {
        _store = store;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<DeadLetterReplayResult> ReplayAsync(Guid failureId, string replayedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(replayedBy))
        {
            replayedBy = "unknown";
        }

        using var activity = DeadLetterMetrics.ActivitySource.StartActivity(
            "dlq.replay", System.Diagnostics.ActivityKind.Producer);
        activity?.SetTag("dlq.failure_id", failureId.ToString());
        activity?.SetTag("dlq.replayed_by", replayedBy);

        var message = await _store.GetAsync(failureId, cancellationToken);
        if (message is null)
        {
            LogReplayRefused(_logger, failureId, "not_found", replayedBy);
            activity?.SetTag("dlq.outcome", "not_found");
            return new DeadLetterReplayResult(DeadLetterReplayOutcome.NotFound, null, "not_found", null);
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
            LogReplayRefused(_logger, failureId, $"status_{message.Status}", replayedBy);
            RecordOutcome(message, outcome: "failure");
            activity?.SetTag("dlq.outcome", $"not_pending_{message.Status}");
            return new DeadLetterReplayResult(DeadLetterReplayOutcome.NotPending, null, $"status_{message.Status}", message);
        }

        Guid newMessageId;
        try
        {
            newMessageId = _publisher.Publish(new DeadLetterReplayRequest(
                OriginalQueue: message.OriginalQueue,
                EventType: message.EventType,
                Payload: message.Payload,
                CorrelationId: message.CorrelationId,
                FailureId: message.Id));
        }
        catch (Exception ex)
        {
            LogReplayPublishFailed(_logger, ex, failureId, replayedBy);
            RecordOutcome(message, outcome: "failure");
            activity?.SetTag("dlq.outcome", "publish_failed");
            return new DeadLetterReplayResult(DeadLetterReplayOutcome.PublishFailed, null, ex.Message, message);
        }

        activity?.SetTag("dlq.new_message_id", newMessageId.ToString());

        var marked = await _store.MarkReplayedAsync(failureId, replayedBy, cancellationToken);
        if (!marked)
        {
            // Lost a race: row was concurrently transitioned. Publish already happened -- count as failure
            // so operators investigate; do not silently swallow.
            LogReplayRefused(_logger, failureId, "concurrent_transition", replayedBy);
            RecordOutcome(message, outcome: "failure");
            activity?.SetTag("dlq.outcome", "concurrent_transition");
            return new DeadLetterReplayResult(DeadLetterReplayOutcome.NotPending, newMessageId, "concurrent_transition", message);
        }

        LogReplaySuccess(_logger, message.Id, message.EventType, message.OriginalQueue, message.Service, newMessageId, replayedBy);
        RecordOutcome(message, outcome: "success");
        activity?.SetTag("dlq.outcome", "success");
        return new DeadLetterReplayResult(DeadLetterReplayOutcome.Success, newMessageId, null, message);
    }

    private static void RecordOutcome(DeadLetterMessage message, string outcome)
    {
        DeadLetterMetrics.Replays.Add(1,
            new KeyValuePair<string, object?>("service", message.Service),
            new KeyValuePair<string, object?>("event_type", message.EventType),
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}
