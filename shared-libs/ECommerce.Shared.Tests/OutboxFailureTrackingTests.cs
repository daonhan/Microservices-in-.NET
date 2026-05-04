using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ECommerce.Shared.Tests;

public sealed class OutboxFailureTrackingTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly OutboxContext _context;

    public OutboxFailureTrackingTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<OutboxContext>(options =>
            options.UseInMemoryDatabase($"outbox-{Guid.NewGuid():N}"));
        services.AddScoped<IOutboxStore>(sp => sp.GetRequiredService<OutboxContext>());
        _provider = services.BuildServiceProvider();
        _context = _provider.GetRequiredService<OutboxContext>();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task Given_pending_event_When_RecordPublishFailure_called_Then_attempts_incremented_and_error_recorded()
    {
        var id = Guid.NewGuid();
        await SeedPending(id);

        await ((IOutboxStore)_context).RecordPublishFailure(id, "boom", maxAttempts: 5);

        var row = await _context.OutboxEvents.FindAsync(id);
        Assert.Equal(1, row!.Attempts);
        Assert.Equal("boom", row.LastError);
        Assert.NotNull(row.LastAttemptAt);
        Assert.Equal(OutboxEventStatus.Pending, row.Status);
    }

    [Fact]
    public async Task Given_event_at_max_minus_one_attempts_When_one_more_failure_Then_status_becomes_Failed()
    {
        var id = Guid.NewGuid();
        await SeedPending(id, attempts: 4);

        await ((IOutboxStore)_context).RecordPublishFailure(id, "still broken", maxAttempts: 5);

        var row = await _context.OutboxEvents.FindAsync(id);
        Assert.Equal(5, row!.Attempts);
        Assert.Equal(OutboxEventStatus.Failed, row.Status);
    }

    [Fact]
    public async Task Given_failed_event_When_GetUnpublishedOutboxEvents_called_Then_failed_row_is_excluded()
    {
        var pendingId = Guid.NewGuid();
        var failedId = Guid.NewGuid();
        await SeedPending(pendingId);
        await SeedPending(failedId, status: OutboxEventStatus.Failed, attempts: 5);

        var pending = await ((IOutboxStore)_context).GetUnpublishedOutboxEvents();
        var failed = await ((IOutboxStore)_context).GetFailedOutboxEvents();

        Assert.Single(pending);
        Assert.Equal(pendingId, pending[0].Id);
        Assert.Single(failed);
        Assert.Equal(failedId, failed[0].Id);
    }

    [Fact]
    public async Task Given_publish_throws_When_OutboxBackgroundService_processes_event_Then_RecordPublishFailure_invoked_and_event_not_marked_published()
    {
        var id = Guid.NewGuid();
        await SeedPending(id);

        var store = Substitute.For<IOutboxStore>();
        store.GetUnpublishedOutboxEvents().Returns(new List<OutboxEvent>
        {
            new()
            {
                Id = id,
                EventType = typeof(SampleEvent).AssemblyQualifiedName!,
                Data = System.Text.Json.JsonSerializer.Serialize(new SampleEvent { Id = id })
            }
        });

        var bus = Substitute.For<IEventBus>();
        bus.PublishAsync(Arg.Any<Event>()).ThrowsAsync(new InvalidOperationException("broker down"));

        var scopeServices = new ServiceCollection();
        scopeServices.AddSingleton(store);
        scopeServices.AddSingleton(bus);
        await using var scopeProvider = scopeServices.BuildServiceProvider();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(scopeProvider);
        scopeFactory.CreateScope().Returns(scope);

        var options = Options.Create(new OutboxOptions { PublishIntervalInSeconds = 1, MaxAttempts = 3 });
        var svc = new OutboxBackgroundService(scopeFactory, options, NullLogger<OutboxBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        var run = svc.StartAsync(cts.Token);
        await Task.Delay(1500);
        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);

        await store.Received().RecordPublishFailure(id, Arg.Is<string>(s => s.Contains("broker down")), 3);
        await store.DidNotReceive().MarkOutboxEventAsPublished(Arg.Any<Guid>());
    }

    private async Task SeedPending(Guid id,
        OutboxEventStatus status = OutboxEventStatus.Pending,
        int attempts = 0)
    {
        _context.OutboxEvents.Add(new OutboxEvent
        {
            Id = id,
            EventType = "Sample",
            Data = "{}",
            Status = status,
            Attempts = attempts
        });
        await _context.SaveChangesAsync();
    }

    private sealed record SampleEvent : Event;
}
