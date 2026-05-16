using System.Diagnostics;
using System.Diagnostics.Metrics;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NSubstitute;

namespace ECommerce.Shared.Tests;

public class OutboxUnitOfWorkTests
{
    private readonly IOutboxStore _outboxStore = Substitute.For<IOutboxStore>();

    private OutboxUnitOfWork CreateSut() => new(_outboxStore);

    /// <summary>
    /// A trivial execution strategy that runs the delegate once, matching the
    /// contract of <see cref="ExecutionStrategy"/> without retry or database.
    /// NSubstitute cannot proxy the ExecuteAsync extension methods, so we
    /// implement the interface member directly.
    /// </summary>
    private sealed class PassthroughExecutionStrategy : IExecutionStrategy
    {
        public bool RetriesOnFailure => false;

        public TResult Execute<TState, TResult>(
            TState state,
            Func<DbContext, TState, TResult> operation,
            Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
            => operation(null!, state);

        public async Task<TResult> ExecuteAsync<TState, TResult>(
            TState state,
            Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
            Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
            CancellationToken cancellationToken = default)
            => await operation(null!, state, cancellationToken);
    }

    private sealed record TestEvent(string Payload) : Event;

    [Fact]
    public async Task Given_WorkReturnsEvents_When_ExecuteAsync_Then_AllEventsAreEnqueued()
    {
        var sut = CreateSut();
        var strategy = new PassthroughExecutionStrategy();
        var event1 = new TestEvent("A");
        var event2 = new TestEvent("B");

        await sut.ExecuteAsync(strategy, () =>
            Task.FromResult<IReadOnlyList<Event>>(new List<Event> { event1, event2 }));

        await _outboxStore.Received(1).AddOutboxEvent(event1);
        await _outboxStore.Received(1).AddOutboxEvent(event2);
    }

    [Fact]
    public async Task Given_WorkCommits_When_ExecuteAsync_Then_ActivityRecordsCommittedOutcome()
    {
        var sut = CreateSut();
        var strategy = new PassthroughExecutionStrategy();
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ECommerce.Shared.Outbox",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => captured.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        await sut.ExecuteAsync(strategy, () =>
            Task.FromResult<IReadOnlyList<Event>>(Array.Empty<Event>()));

        var activity = captured.FirstOrDefault(a =>
            a.OperationName == "outbox.uow"
            && (string?)a.GetTagItem("outbox.outcome") == "committed");
        Assert.NotNull(activity);
        Assert.Equal("execute", activity.GetTagItem("outbox.operation"));
        Assert.Equal("committed", activity.GetTagItem("outbox.outcome"));
    }

    [Fact]
    public async Task Given_WorkThrows_When_ExecuteAsync_Then_ActivityRecordsRolledBackOutcome()
    {
        var sut = CreateSut();
        var strategy = new PassthroughExecutionStrategy();
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ECommerce.Shared.Outbox",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => captured.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(strategy, () =>
                throw new InvalidOperationException("boom")));

        var activity = captured.FirstOrDefault(a =>
            a.OperationName == "outbox.uow"
            && (string?)a.GetTagItem("outbox.outcome") == "rolled_back");
        Assert.NotNull(activity);
        Assert.Equal("execute", activity.GetTagItem("outbox.operation"));
        Assert.Equal("rolled_back", activity.GetTagItem("outbox.outcome"));
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("error.type"));
    }

    [Theory]
    [InlineData(false, "committed")]
    [InlineData(true, "rolled_back")]
    public async Task Given_WorkCompletesOrThrows_When_ExecuteAsync_Then_TransactionMetricRecordsOutcome(
        bool shouldThrow,
        string expectedOutcome)
    {
        var sut = CreateSut();
        var strategy = new PassthroughExecutionStrategy();
        var capturedTags = new List<KeyValuePair<string, object?>>();
        var metricEmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "ECommerce.Shared.Outbox"
                && instrument.Name == "outbox_uow_transactions_total")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var matchedOutcome = false;
            lock (capturedTags)
            {
                foreach (var tag in tags)
                {
                    capturedTags.Add(new KeyValuePair<string, object?>(tag.Key, tag.Value));
                    if (tag.Key == "outcome" && (string?)tag.Value == expectedOutcome)
                    {
                        matchedOutcome = true;
                    }
                }
            }

            if (matchedOutcome)
            {
                metricEmitted.TrySetResult();
            }
        });
        meterListener.Start();

        if (shouldThrow)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sut.ExecuteAsync(strategy, () =>
                    throw new InvalidOperationException("boom")));
        }
        else
        {
            await sut.ExecuteAsync(strategy, () =>
                Task.FromResult<IReadOnlyList<Event>>(Array.Empty<Event>()));
        }

        var metricSeen = await Task.WhenAny(metricEmitted.Task, Task.Delay(TimeSpan.FromSeconds(3)))
            == metricEmitted.Task;

        Assert.True(metricSeen, $"outbox_uow_transactions_total was not tagged with outcome={expectedOutcome}.");

        KeyValuePair<string, object?>[] snapshot;
        lock (capturedTags)
        {
            snapshot = capturedTags.ToArray();
        }

        Assert.Contains(snapshot, t => t.Key == "operation" && (string?)t.Value == "execute");
        Assert.Contains(snapshot, t => t.Key == "outcome" && (string?)t.Value == expectedOutcome);
    }

    [Fact]
    public async Task Given_WorkReturnsEmptyList_When_ExecuteAsync_Then_NoEventsEnqueued()
    {
        var sut = CreateSut();
        var strategy = new PassthroughExecutionStrategy();

        await sut.ExecuteAsync(strategy, () =>
            Task.FromResult<IReadOnlyList<Event>>(Array.Empty<Event>()));

        await _outboxStore.DidNotReceive().AddOutboxEvent(Arg.Any<Event>());
    }

    [Fact]
    public async Task Given_WorkThrows_When_ExecuteAsync_Then_NoEventsEnqueued()
    {
        var sut = CreateSut();
        var strategy = new PassthroughExecutionStrategy();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(strategy, () =>
                throw new InvalidOperationException("boom")));

        await _outboxStore.DidNotReceive().AddOutboxEvent(Arg.Any<Event>());
    }

    [Fact]
    public async Task Given_NullStrategy_When_ExecuteAsync_Then_Throws()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.ExecuteAsync(null!, () =>
                Task.FromResult<IReadOnlyList<Event>>(Array.Empty<Event>())));
    }

    [Fact]
    public async Task Given_NullWork_When_ExecuteAsync_Then_Throws()
    {
        var sut = CreateSut();
        var strategy = new PassthroughExecutionStrategy();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.ExecuteAsync(strategy, null!));
    }
}
