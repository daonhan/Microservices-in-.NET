using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGateway.Infrastructure.Seeding;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;

namespace ApiGateway.Tests.Integration;

public class DlqOperatorFixtureIntegrationTests : IAsyncLifetime, IDisposable
{
    private StubHttpServer _downstreamStub = null!;

    public async Task InitializeAsync()
    {
        _downstreamStub = new StubHttpServer();
        await _downstreamStub.StartAsync();
    }

    public async Task DisposeAsync() => await _downstreamStub.DisposeAsync();

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact]
    public async Task Given_seeded_fixtures_and_operator_jwt_When_listing_Then_returns_200_with_all_five()
    {
        var (store, _, _) = SeedFixtureBackend();
        await using var harness = await GatewayTestHarness.CreateAsync(
            "Yarp", _downstreamStub.BaseUrl,
            deadLetterStore: store);
        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", harness.CreateJwt("Operator"));

        var response = await harness.Client.GetAsync(
            $"/operator/api/failures?service={DeadLetterQaFixtureSeeder.OperatorService}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<DeadLetterPage>();
        Assert.NotNull(page);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(5, page.TotalCount);
        Assert.Contains(page.Items, m => m.Id == DeadLetterQaFixtureSeeder.ListId);
    }

    [Fact]
    public async Task Given_seeded_fixtures_and_operator_jwt_When_getting_detail_Then_returns_200_with_matching_id()
    {
        var (store, _, _) = SeedFixtureBackend();
        await using var harness = await GatewayTestHarness.CreateAsync(
            "Yarp", _downstreamStub.BaseUrl,
            deadLetterStore: store);
        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", harness.CreateJwt("Operator"));

        var response = await harness.Client.GetAsync(
            $"/operator/api/failures/{DeadLetterQaFixtureSeeder.ListId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var messageId = doc.RootElement.GetProperty("message").GetProperty("id").GetGuid();
        Assert.Equal(DeadLetterQaFixtureSeeder.ListId, messageId);
    }

    [Fact]
    public async Task Given_seeded_fixtures_and_operator_jwt_When_replaying_Then_returns_202_and_marks_Replayed()
    {
        var (store, replayer, _) = SeedFixtureBackend();
        await using var harness = await GatewayTestHarness.CreateAsync(
            "Yarp", _downstreamStub.BaseUrl,
            deadLetterStore: store,
            deadLetterReplayer: replayer);
        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", harness.CreateJwt("Operator"));

        var response = await harness.Client.PostAsync(
            $"/operator/api/failures/{DeadLetterQaFixtureSeeder.ReplayId}/replay",
            content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("newMessageId", out var newId));
        Assert.NotEqual(Guid.Empty, newId.GetGuid());

        var replayed = store.Snapshot().Single(m => m.Id == DeadLetterQaFixtureSeeder.ReplayId);
        Assert.Equal(DeadLetterStatus.Replayed, replayed.Status);
    }

    [Fact]
    public async Task Given_seeded_fixtures_and_operator_jwt_When_batch_replaying_Then_returns_200_with_two_success_items()
    {
        var (store, replayer, _) = SeedFixtureBackend();
        await using var harness = await GatewayTestHarness.CreateAsync(
            "Yarp", _downstreamStub.BaseUrl,
            deadLetterStore: store,
            deadLetterReplayer: replayer);
        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", harness.CreateJwt("Operator"));

        var response = await harness.Client.PostAsJsonAsync(
            "/operator/api/failures/replay-batch",
            new
            {
                ids = new[]
                {
                    DeadLetterQaFixtureSeeder.BatchReplayAId,
                    DeadLetterQaFixtureSeeder.BatchReplayBId
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            Assert.Equal("success", item.GetProperty("status").GetString());
            Assert.NotEqual(Guid.Empty, item.GetProperty("newMessageId").GetGuid());
        }

        var snapshot = store.Snapshot();
        Assert.Equal(DeadLetterStatus.Replayed,
            snapshot.Single(m => m.Id == DeadLetterQaFixtureSeeder.BatchReplayAId).Status);
        Assert.Equal(DeadLetterStatus.Replayed,
            snapshot.Single(m => m.Id == DeadLetterQaFixtureSeeder.BatchReplayBId).Status);
    }

    [Fact]
    public async Task Given_seeded_fixtures_and_operator_jwt_When_discarding_Then_returns_202_and_marks_Discarded()
    {
        var (store, _, discarder) = SeedFixtureBackend();
        await using var harness = await GatewayTestHarness.CreateAsync(
            "Yarp", _downstreamStub.BaseUrl,
            deadLetterStore: store,
            deadLetterDiscarder: discarder);
        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", harness.CreateJwt("Operator"));

        var response = await harness.Client.PostAsJsonAsync(
            $"/operator/api/failures/{DeadLetterQaFixtureSeeder.DiscardId}/discard",
            new { reason = "qa smoke discard" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var discarded = store.Snapshot().Single(m => m.Id == DeadLetterQaFixtureSeeder.DiscardId);
        Assert.Equal(DeadLetterStatus.Discarded, discarded.Status);
        Assert.Equal("qa smoke discard", discarded.DiscardReason);
    }

    private static (ListBackedDeadLetterStore Store, ListBackedReplayer Replayer, ListBackedDiscarder Discarder) SeedFixtureBackend()
    {
        var fixtures = new[]
        {
            DeadLetterQaFixtureSeeder.ListId,
            DeadLetterQaFixtureSeeder.ReplayId,
            DeadLetterQaFixtureSeeder.BatchReplayAId,
            DeadLetterQaFixtureSeeder.BatchReplayBId,
            DeadLetterQaFixtureSeeder.DiscardId,
        }.Select(id => new DeadLetterMessage
        {
            Id = id,
            EventType = DeadLetterQaFixtureSeeder.OperatorEventType,
            RoutingKey = DeadLetterQaFixtureSeeder.InertReplaySinkQueue,
            OriginalQueue = DeadLetterQaFixtureSeeder.InertReplaySinkQueue,
            Service = DeadLetterQaFixtureSeeder.OperatorService,
            Payload = "{}",
            FailureReason = "qa seed",
            Attempts = 0,
            FailedAt = DeadLetterQaFixtureSeeder.SeedFailedAt,
            Status = DeadLetterStatus.Pending,
            CorrelationId = DeadLetterQaFixtureSeeder.SeedCorrelationId,
            Origin = DeadLetterOrigin.DeadLetter,
        }).ToList();

        var store = new ListBackedDeadLetterStore(fixtures);
        var replayer = new ListBackedReplayer(store);
        var discarder = new ListBackedDiscarder(store);
        return (store, replayer, discarder);
    }
}

internal sealed class ListBackedDeadLetterStore : IDeadLetterStore
{
    private readonly List<DeadLetterMessage> _rows;
    private readonly object _gate = new();

    public ListBackedDeadLetterStore(IEnumerable<DeadLetterMessage> rows)
    {
        _rows = rows.ToList();
    }

    public IReadOnlyList<DeadLetterMessage> Snapshot()
    {
        lock (_gate)
        {
            return _rows.ToList();
        }
    }

    public Task CaptureAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _rows.Add(message);
        }
        return Task.CompletedTask;
    }

    public Task<DeadLetterPage> ListAsync(DeadLetterFilter filter, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IEnumerable<DeadLetterMessage> query = _rows;
            if (!string.IsNullOrWhiteSpace(filter.Service))
            {
                query = query.Where(m => m.Service == filter.Service);
            }
            var items = query.ToList();
            return Task.FromResult(new DeadLetterPage(items, filter.Page, filter.PageSize, items.Count));
        }
    }

    public Task<DeadLetterMessage?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_rows.FirstOrDefault(m => m.Id == id));
        }
    }

    public Task<bool> MarkReplayedAsync(Guid id, string replayedBy, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var row = _rows.FirstOrDefault(m => m.Id == id);
            if (row is null || row.Status != DeadLetterStatus.Pending)
            {
                return Task.FromResult(false);
            }
            row.Status = DeadLetterStatus.Replayed;
            row.ReplayedAt = DateTime.UtcNow;
            row.ReplayedBy = replayedBy;
            return Task.FromResult(true);
        }
    }

    public Task<bool> MarkDiscardedAsync(Guid id, string discardedBy, string discardReason, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var row = _rows.FirstOrDefault(m => m.Id == id);
            if (row is null || row.Status != DeadLetterStatus.Pending)
            {
                return Task.FromResult(false);
            }
            row.Status = DeadLetterStatus.Discarded;
            row.DiscardedAt = DateTime.UtcNow;
            row.DiscardedBy = discardedBy;
            row.DiscardReason = discardReason;
            return Task.FromResult(true);
        }
    }
}

internal sealed class ListBackedReplayer : IDeadLetterReplayer
{
    private readonly ListBackedDeadLetterStore _store;

    public ListBackedReplayer(ListBackedDeadLetterStore store)
    {
        _store = store;
    }

    public async Task<DeadLetterReplayResult> ReplayAsync(Guid failureId, string replayedBy, CancellationToken cancellationToken = default)
    {
        var row = await _store.GetAsync(failureId, cancellationToken);
        if (row is null)
        {
            return new DeadLetterReplayResult(DeadLetterReplayOutcome.NotFound, null, "not_found", null);
        }
        if (row.Status != DeadLetterStatus.Pending)
        {
            return new DeadLetterReplayResult(DeadLetterReplayOutcome.NotPending, null, $"status_{row.Status}", row);
        }
        var marked = await _store.MarkReplayedAsync(failureId, replayedBy, cancellationToken);
        if (!marked)
        {
            return new DeadLetterReplayResult(DeadLetterReplayOutcome.NotPending, null, "race", row);
        }
        return new DeadLetterReplayResult(DeadLetterReplayOutcome.Success, Guid.NewGuid(), null, row);
    }
}

internal sealed class ListBackedDiscarder : IDeadLetterDiscarder
{
    private readonly ListBackedDeadLetterStore _store;

    public ListBackedDiscarder(ListBackedDeadLetterStore store)
    {
        _store = store;
    }

    public async Task<DeadLetterDiscardResult> DiscardAsync(Guid failureId, string discardedBy, string discardReason, CancellationToken cancellationToken = default)
    {
        var row = await _store.GetAsync(failureId, cancellationToken);
        if (row is null)
        {
            return new DeadLetterDiscardResult(DeadLetterDiscardOutcome.NotFound, "not_found", null);
        }
        if (row.Status != DeadLetterStatus.Pending)
        {
            return new DeadLetterDiscardResult(DeadLetterDiscardOutcome.NotPending, $"status_{row.Status}", row);
        }
        var marked = await _store.MarkDiscardedAsync(failureId, discardedBy, discardReason, cancellationToken);
        if (!marked)
        {
            return new DeadLetterDiscardResult(DeadLetterDiscardOutcome.NotPending, "race", row);
        }
        return new DeadLetterDiscardResult(DeadLetterDiscardOutcome.Success, null, row);
    }
}
