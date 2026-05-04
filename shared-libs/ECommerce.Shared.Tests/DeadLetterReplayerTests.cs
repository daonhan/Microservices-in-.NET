using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ECommerce.Shared.Tests;

public sealed class DeadLetterReplayerTests
{
    private static DeadLetterMessage NewPending(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        EventType = "OrderCreatedEvent",
        RoutingKey = "OrderCreatedEvent",
        OriginalQueue = "basket",
        Service = "basket",
        Payload = "{\"hello\":1}",
        FailureReason = "boom",
        Attempts = 3,
        FailedAt = DateTime.UtcNow,
        Status = DeadLetterStatus.Pending,
        CorrelationId = Guid.NewGuid()
    };

    private static (DeadLetterReplayer replayer, IDeadLetterStore store, IDeadLetterPublisher publisher) Build()
    {
        var store = Substitute.For<IDeadLetterStore>();
        var publisher = Substitute.For<IDeadLetterPublisher>();
        var replayer = new DeadLetterReplayer(store, publisher, NullLogger<DeadLetterReplayer>.Instance);
        return (replayer, store, publisher);
    }

    [Fact]
    public async Task Given_unknown_failure_id_When_ReplayAsync_Then_returns_NotFound_and_does_not_publish()
    {
        var (replayer, store, publisher) = Build();
        var id = Guid.NewGuid();
        store.GetAsync(id, Arg.Any<CancellationToken>()).Returns((DeadLetterMessage?)null);

        var result = await replayer.ReplayAsync(id, "alice");

        Assert.Equal(DeadLetterReplayOutcome.NotFound, result.Outcome);
        Assert.Null(result.NewMessageId);
        publisher.DidNotReceive().Publish(Arg.Any<DeadLetterReplayRequest>());
        await store.DidNotReceive().MarkReplayedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_already_replayed_message_When_ReplayAsync_Then_returns_NotPending_and_does_not_publish()
    {
        var (replayer, store, publisher) = Build();
        var msg = NewPending();
        msg.Status = DeadLetterStatus.Replayed;
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);

        var result = await replayer.ReplayAsync(msg.Id, "alice");

        Assert.Equal(DeadLetterReplayOutcome.NotPending, result.Outcome);
        publisher.DidNotReceive().Publish(Arg.Any<DeadLetterReplayRequest>());
    }

    [Fact]
    public async Task Given_pending_message_When_ReplayAsync_Then_publishes_to_OriginalQueue_and_marks_replayed()
    {
        var (replayer, store, publisher) = Build();
        var msg = NewPending();
        var newId = Guid.NewGuid();
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);
        store.MarkReplayedAsync(msg.Id, "alice", Arg.Any<CancellationToken>()).Returns(true);
        publisher.Publish(Arg.Any<DeadLetterReplayRequest>()).Returns(newId);

        var result = await replayer.ReplayAsync(msg.Id, "alice");

        Assert.Equal(DeadLetterReplayOutcome.Success, result.Outcome);
        Assert.Equal(newId, result.NewMessageId);
        publisher.Received(1).Publish(Arg.Is<DeadLetterReplayRequest>(r =>
            r.OriginalQueue == "basket" &&
            r.EventType == "OrderCreatedEvent" &&
            r.Payload == msg.Payload &&
            r.CorrelationId == msg.CorrelationId &&
            r.FailureId == msg.Id));
        await store.Received(1).MarkReplayedAsync(msg.Id, "alice", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_publisher_throws_When_ReplayAsync_Then_returns_PublishFailed_and_does_not_mark_replayed()
    {
        var (replayer, store, publisher) = Build();
        var msg = NewPending();
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);
        publisher.Publish(Arg.Any<DeadLetterReplayRequest>()).Returns(_ => throw new InvalidOperationException("broker down"));

        var result = await replayer.ReplayAsync(msg.Id, "alice");

        Assert.Equal(DeadLetterReplayOutcome.PublishFailed, result.Outcome);
        Assert.Null(result.NewMessageId);
        Assert.Contains("broker down", result.FailureReason);
        await store.DidNotReceive().MarkReplayedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_blank_replayedBy_When_ReplayAsync_Then_falls_back_to_unknown()
    {
        var (replayer, store, publisher) = Build();
        var msg = NewPending();
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);
        store.MarkReplayedAsync(msg.Id, "unknown", Arg.Any<CancellationToken>()).Returns(true);
        publisher.Publish(Arg.Any<DeadLetterReplayRequest>()).Returns(Guid.NewGuid());

        var result = await replayer.ReplayAsync(msg.Id, "   ");

        Assert.Equal(DeadLetterReplayOutcome.Success, result.Outcome);
        await store.Received(1).MarkReplayedAsync(msg.Id, "unknown", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_pending_message_When_ReplayAsync_Then_emits_activity_tagged_with_correlation_id_and_outcome()
    {
        var (replayer, store, publisher) = Build();
        var msg = NewPending();
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);
        store.MarkReplayedAsync(msg.Id, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        publisher.Publish(Arg.Any<DeadLetterReplayRequest>()).Returns(Guid.NewGuid());

        var captured = new List<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "ECommerce.Shared.DeadLetter",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => captured.Add(activity),
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var result = await replayer.ReplayAsync(msg.Id, "alice");

        Assert.Equal(DeadLetterReplayOutcome.Success, result.Outcome);
        var replayActivity = Assert.Single(captured, a => a.OperationName == "dlq.replay");
        Assert.Equal(msg.CorrelationId!.Value.ToString(), replayActivity.GetTagItem("messaging.correlation_id"));
        Assert.Equal(msg.Id.ToString(), replayActivity.GetTagItem("dlq.failure_id"));
        Assert.Equal(msg.EventType, replayActivity.GetTagItem("dlq.event_type"));
        Assert.Equal(msg.OriginalQueue, replayActivity.GetTagItem("dlq.original_queue"));
        Assert.Equal("success", replayActivity.GetTagItem("dlq.outcome"));
    }
}
