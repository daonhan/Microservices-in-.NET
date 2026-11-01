using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace ECommerce.Shared.Tests;

[Trait("Category", "Integration")]
public sealed class OutboxDispatchIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        // READPAST must also work on Azure SQL-style snapshot read committed.
        await context.Database.ExecuteSqlRawAsync("ALTER DATABASE [OutboxReliability] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
    }

    public async Task DisposeAsync() => await _sql.DisposeAsync();

    private OutboxContext CreateContext() => new(new DbContextOptionsBuilder<OutboxContext>()
        .UseSqlServer(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_sql.GetConnectionString())
        { InitialCatalog = "OutboxReliability" }.ConnectionString).Options);

    [Fact]
    public async Task Given_two_replicas_When_one_holds_publish_lock_Then_other_skips_row_and_publishes_another()
    {
        var first = new TestEvent();
        var second = new TestEvent();
        await using var owner = CreateContext();
        await owner.AddOutboxEvent(first);
        await owner.AddOutboxEvent(second);
        await using var competitor = CreateContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishing = owner.DispatchAsync(first.Id, async _ =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }, 3);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var candidates = await competitor.GetUnpublishedOutboxEvents().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(candidates, row => row.Id == first.Id);
            await competitor.DispatchAsync(first.Id, _ => throw new InvalidOperationException("duplicate publish"), 3)
                .WaitAsync(TimeSpan.FromSeconds(5));
            var calls = 0;
            await competitor.DispatchAsync(second.Id, _ => { calls++; return Task.CompletedTask; }, 3);
            Assert.Equal(1, calls);
        }
        finally
        {
            release.TrySetResult();
            await publishing;
        }

        await using var verification = CreateContext();
        Assert.True((await verification.OutboxEvents.FindAsync(first.Id))!.Sent);
        Assert.True((await verification.OutboxEvents.FindAsync(second.Id))!.Sent);
        Assert.Equal(0, (await verification.OutboxEvents.FindAsync(first.Id))!.Attempts);
    }

    [Fact]
    public async Task Given_rollback_after_publish_When_dispatched_again_Then_row_remains_retryable()
    {
        var @event = new TestEvent();
        await using var context = CreateContext();
        await context.AddOutboxEvent(@event);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.DispatchAsync(@event.Id, _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }, 3, cancellation.Token));

        await using var recovered = CreateContext();
        var pending = await recovered.OutboxEvents.FindAsync(@event.Id);
        Assert.False(pending!.Sent);
        Assert.Equal(0, pending.Attempts);
        await recovered.DispatchAsync(@event.Id, _ => Task.CompletedTask, 3);
        Assert.True((await recovered.OutboxEvents.FindAsync(@event.Id))!.Sent);
    }

    [Fact]
    public async Task Given_publish_failure_When_retried_to_limit_Then_failure_is_recorded_without_marking_sent()
    {
        var @event = new TestEvent();
        await using var context = CreateContext();
        await context.AddOutboxEvent(@event);
        for (var i = 0; i < 2; i++)
        {
            await context.DispatchAsync(@event.Id, _ => throw new InvalidOperationException("broker nack"), 2);
        }
        await context.DispatchAsync(@event.Id, _ => throw new InvalidOperationException("must not retry failed row"), 2);
        var row = await context.OutboxEvents.FindAsync(@event.Id);
        Assert.False(row!.Sent);
        Assert.Equal(2, row.Attempts);
        Assert.Equal(OutboxEventStatus.Failed, row.Status);
        Assert.Contains("broker nack", row.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_ambient_rollback_When_same_event_added_again_Then_identity_map_does_not_hide_missing_row()
    {
        var @event = new TestEvent();
        await using var context = CreateContext();
        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await context.AddOutboxEvent(@event);
        }
        Assert.False(await context.OutboxEvents.AnyAsync(row => row.Id == @event.Id));
        await context.AddOutboxEvent(@event);
        await context.AddOutboxEvent(@event);
        Assert.Equal(1, await context.OutboxEvents.CountAsync(row => row.Id == @event.Id));
    }

    private sealed record TestEvent : Event;
}
