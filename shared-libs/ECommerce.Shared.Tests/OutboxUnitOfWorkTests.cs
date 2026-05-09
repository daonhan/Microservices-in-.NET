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
