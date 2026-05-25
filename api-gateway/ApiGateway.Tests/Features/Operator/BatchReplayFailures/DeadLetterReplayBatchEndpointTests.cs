using System.Security.Claims;
using ApiGateway.Features.Operator.BatchReplayFailures;
using ApiGateway.Infrastructure.Auth;
using ECommerce.Shared.Infrastructure.DeadLetter;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ApiGateway.Tests.Features.Operator.BatchReplayFailures;

public sealed class DeadLetterReplayBatchEndpointTests
{
    private static int StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 0;

    private static ClaimsPrincipal AsUser(string sub) =>
        new(new ClaimsIdentity([new Claim(JwtClaimTypes.Subject, sub)]));

    [Fact]
    public async Task Given_null_request_When_BatchReplay_Then_returns_BadRequest_and_does_not_call_replayer()
    {
        var replayer = new FakeReplayer();
        var handler = new BatchReplayFailuresHandler(replayer);

        var result = await handler.HandleAsync(request: null, AsUser("alice"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, replayer.CallCount);
    }

    [Fact]
    public async Task Given_empty_ids_When_BatchReplay_Then_returns_BadRequest_and_does_not_call_replayer()
    {
        var replayer = new FakeReplayer();
        var handler = new BatchReplayFailuresHandler(replayer);

        var result = await handler.HandleAsync(new BatchReplayRequest(Array.Empty<Guid>()), AsUser("alice"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, replayer.CallCount);
    }

    [Fact]
    public async Task Given_mixed_outcomes_When_BatchReplay_Then_returns_Ok_with_per_id_results_and_calls_replayer_for_each()
    {
        var ok = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var conflict = Guid.NewGuid();
        var failed = Guid.NewGuid();

        var newMessageId = Guid.NewGuid();
        var replayer = new FakeReplayer
        {
            Results =
            {
                [ok] = new DeadLetterReplayResult(DeadLetterReplayOutcome.Success, newMessageId, null, null),
                [missing] = new DeadLetterReplayResult(DeadLetterReplayOutcome.NotFound, null, "not_found", null),
                [conflict] = new DeadLetterReplayResult(DeadLetterReplayOutcome.NotPending, null, "status_Replayed", null),
                [failed] = new DeadLetterReplayResult(DeadLetterReplayOutcome.PublishFailed, null, "broker_down", null)
            }
        };
        var handler = new BatchReplayFailuresHandler(replayer);

        var result = await handler.HandleAsync(
            new BatchReplayRequest([ok, missing, conflict, failed]),
            AsUser("alice"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        var ok200 = Assert.IsType<Ok<BatchReplayResponse>>(result);
        var response = Assert.IsType<BatchReplayResponse>(ok200.Value);
        Assert.Equal(4, response.Items.Count);
        Assert.Equal("success", response.Items.Single(i => i.Id == ok).Status);
        Assert.Equal(newMessageId, response.Items.Single(i => i.Id == ok).NewMessageId);
        Assert.Equal("not_found", response.Items.Single(i => i.Id == missing).Status);
        Assert.Equal("not_pending", response.Items.Single(i => i.Id == conflict).Status);
        Assert.Equal("publish_failed", response.Items.Single(i => i.Id == failed).Status);

        Assert.Equal(4, replayer.CallCount);
        Assert.Equal(new[] { ok, missing, conflict, failed }, replayer.SeenIds);
        Assert.All(replayer.SeenReplayedBy, by => Assert.Equal("alice", by));
    }

    private sealed class FakeReplayer : IDeadLetterReplayer
    {
        public Dictionary<Guid, DeadLetterReplayResult> Results { get; } = new();

        public int CallCount { get; private set; }

        public List<Guid> SeenIds { get; } = new();

        public List<string> SeenReplayedBy { get; } = new();

        public Task<DeadLetterReplayResult> ReplayAsync(Guid failureId, string replayedBy, CancellationToken cancellationToken = default)
        {
            CallCount++;
            SeenIds.Add(failureId);
            SeenReplayedBy.Add(replayedBy);
            var result = Results.TryGetValue(failureId, out var r)
                ? r
                : new DeadLetterReplayResult(DeadLetterReplayOutcome.Success, Guid.NewGuid(), null, null);
            return Task.FromResult(result);
        }
    }
}
