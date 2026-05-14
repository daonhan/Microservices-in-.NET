using System.Diagnostics.Metrics;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using ECommerce.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ECommerce.Shared.Tests;

public sealed class DeadLetterDiscarderTests
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

    private static (DeadLetterDiscarder discarder, IDeadLetterStore store) Build()
    {
        var store = Substitute.For<IDeadLetterStore>();
        var discarder = new DeadLetterDiscarder(store, NullLogger<DeadLetterDiscarder>.Instance);
        return (discarder, store);
    }

    [Fact]
    public async Task Given_blank_reason_When_DiscardAsync_Then_returns_ReasonRequired_and_does_not_touch_store()
    {
        var (discarder, store) = Build();

        var result = await discarder.DiscardAsync(Guid.NewGuid(), "alice", "   ");

        Assert.Equal(DeadLetterDiscardOutcome.ReasonRequired, result.Outcome);
        await store.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkDiscardedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_unknown_failure_id_When_DiscardAsync_Then_returns_NotFound()
    {
        var (discarder, store) = Build();
        var id = Guid.NewGuid();
        store.GetAsync(id, Arg.Any<CancellationToken>()).Returns((DeadLetterMessage?)null);

        var result = await discarder.DiscardAsync(id, "alice", "duplicate event");

        Assert.Equal(DeadLetterDiscardOutcome.NotFound, result.Outcome);
        await store.DidNotReceive().MarkDiscardedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_already_discarded_message_When_DiscardAsync_Then_returns_NotPending()
    {
        var (discarder, store) = Build();
        var msg = NewPending();
        msg.Status = DeadLetterStatus.Discarded;
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);

        var result = await discarder.DiscardAsync(msg.Id, "alice", "duplicate event");

        Assert.Equal(DeadLetterDiscardOutcome.NotPending, result.Outcome);
        await store.DidNotReceive().MarkDiscardedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_pending_message_When_DiscardAsync_Then_marks_discarded_with_metadata()
    {
        var (discarder, store) = Build();
        var msg = NewPending();
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);
        store.MarkDiscardedAsync(msg.Id, "alice", "duplicate event", Arg.Any<CancellationToken>()).Returns(true);

        var result = await discarder.DiscardAsync(msg.Id, "alice", "duplicate event");

        Assert.Equal(DeadLetterDiscardOutcome.Success, result.Outcome);
        Assert.Same(msg, result.Message);
        await store.Received(1).MarkDiscardedAsync(msg.Id, "alice", "duplicate event", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_concurrent_transition_When_DiscardAsync_Then_returns_NotPending()
    {
        var (discarder, store) = Build();
        var msg = NewPending();
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);
        store.MarkDiscardedAsync(msg.Id, "alice", "duplicate event", Arg.Any<CancellationToken>()).Returns(false);

        var result = await discarder.DiscardAsync(msg.Id, "alice", "duplicate event");

        Assert.Equal(DeadLetterDiscardOutcome.NotPending, result.Outcome);
        Assert.Equal("concurrent_transition", result.FailureReason);
    }

    [Fact]
    public async Task Given_blank_discardedBy_When_DiscardAsync_Then_falls_back_to_unknown()
    {
        var (discarder, store) = Build();
        var msg = NewPending();
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);
        store.MarkDiscardedAsync(msg.Id, "unknown", "duplicate event", Arg.Any<CancellationToken>()).Returns(true);

        var result = await discarder.DiscardAsync(msg.Id, "  ", "duplicate event");

        Assert.Equal(DeadLetterDiscardOutcome.Success, result.Outcome);
        await store.Received(1).MarkDiscardedAsync(msg.Id, "unknown", "duplicate event", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_pending_message_When_DiscardAsync_Then_dlq_discards_total_is_tagged_with_RabbitMq_provider()
    {
        var (discarder, store) = Build();
        var msg = NewPending();
        store.GetAsync(msg.Id, Arg.Any<CancellationToken>()).Returns(msg);
        store.MarkDiscardedAsync(msg.Id, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var capturedTags = new List<KeyValuePair<string, object?>>();
        var metricEmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DeadLetterMetrics.MeterName
                && instrument.Name == "dlq_discards_total")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var matchedProvider = false;
            lock (capturedTags)
            {
                foreach (var tag in tags)
                {
                    capturedTags.Add(new KeyValuePair<string, object?>(tag.Key, tag.Value));
                    if (tag.Key == "provider" && (string?)tag.Value == MessagingOptions.RabbitMqProvider)
                    {
                        matchedProvider = true;
                    }
                }
            }

            if (matchedProvider)
            {
                metricEmitted.TrySetResult();
            }
        });
        meterListener.Start();

        var result = await discarder.DiscardAsync(msg.Id, "alice", "duplicate event");
        var metricSeen = await Task.WhenAny(metricEmitted.Task, Task.Delay(TimeSpan.FromSeconds(3)))
            == metricEmitted.Task;

        Assert.Equal(DeadLetterDiscardOutcome.Success, result.Outcome);
        Assert.True(metricSeen, "dlq_discards_total was not tagged with provider=RabbitMq.");

        KeyValuePair<string, object?>[] snapshot;
        lock (capturedTags)
        {
            snapshot = capturedTags.ToArray();
        }

        Assert.Contains(snapshot, t => t.Key == "provider" && (string?)t.Value == MessagingOptions.RabbitMqProvider);
        Assert.Contains(snapshot, t => t.Key == "service" && (string?)t.Value == msg.Service);
        Assert.Contains(snapshot, t => t.Key == "event_type" && (string?)t.Value == msg.EventType);
        Assert.Contains(snapshot, t => t.Key == "outcome" && (string?)t.Value == "success");
    }
}
