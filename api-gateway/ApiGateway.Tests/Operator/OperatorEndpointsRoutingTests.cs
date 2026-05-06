using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiGateway.Tests.Integration;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;

namespace ApiGateway.Tests.Operator;

public class OperatorEndpointsRoutingTests : IAsyncLifetime, IDisposable
{
    private StubHttpServer _downstreamStub = null!;

    public async Task InitializeAsync()
    {
        _downstreamStub = new StubHttpServer();
        await _downstreamStub.StartAsync();
    }

    public async Task DisposeAsync() => await _downstreamStub.DisposeAsync();

    public void Dispose() => GC.SuppressFinalize(this);

    [Theory]
    [InlineData("Yarp")]
    [InlineData("Ocelot")]
    public async Task Given_operator_token_When_PostDiscard_Then_endpoint_is_reachable_under_provider(string provider)
    {
        var discarder = new RecordingDiscarder
        {
            Result = new DeadLetterDiscardResult(DeadLetterDiscardOutcome.Success, null, null)
        };

        await using var harness = await GatewayTestHarness.CreateAsync(
            provider,
            _downstreamStub.BaseUrl,
            deadLetterDiscarder: discarder);

        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", harness.CreateJwt("Operator"));

        var failureId = Guid.NewGuid();
        var response = await harness.Client.PostAsJsonAsync(
            $"/operator/api/failures/{failureId}/discard",
            new { reason = "duplicate event" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, discarder.CallCount);
        Assert.Equal(failureId, discarder.LastFailureId);
    }

    [Theory]
    [InlineData("Yarp")]
    [InlineData("Ocelot")]
    public async Task Given_operator_token_When_PostReplayBatch_Then_endpoint_is_reachable_under_provider(string provider)
    {
        var replayer = new RecordingReplayer();

        await using var harness = await GatewayTestHarness.CreateAsync(
            provider,
            _downstreamStub.BaseUrl,
            deadLetterReplayer: replayer);

        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", harness.CreateJwt("Operator"));

        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var response = await harness.Client.PostAsJsonAsync(
            "/operator/api/failures/replay-batch",
            new { ids });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ids, replayer.SeenIds);
    }

    [Theory]
    [InlineData("Yarp")]
    [InlineData("Ocelot")]
    public async Task Given_no_token_When_PostDiscard_Then_returns_Unauthorized_under_provider(string provider)
    {
        var discarder = new RecordingDiscarder();

        await using var harness = await GatewayTestHarness.CreateAsync(
            provider,
            _downstreamStub.BaseUrl,
            deadLetterDiscarder: discarder);

        var response = await harness.Client.PostAsJsonAsync(
            $"/operator/api/failures/{Guid.NewGuid()}/discard",
            new { reason = "duplicate event" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, discarder.CallCount);
    }

    [Theory]
    [InlineData("Yarp")]
    [InlineData("Ocelot")]
    public async Task Given_no_token_When_PostReplayBatch_Then_returns_Unauthorized_under_provider(string provider)
    {
        var replayer = new RecordingReplayer();

        await using var harness = await GatewayTestHarness.CreateAsync(
            provider,
            _downstreamStub.BaseUrl,
            deadLetterReplayer: replayer);

        var response = await harness.Client.PostAsJsonAsync(
            "/operator/api/failures/replay-batch",
            new { ids = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(replayer.SeenIds);
    }

    private sealed class RecordingDiscarder : IDeadLetterDiscarder
    {
        public DeadLetterDiscardResult Result { get; set; } =
            new DeadLetterDiscardResult(DeadLetterDiscardOutcome.Success, null, null);

        public int CallCount { get; private set; }

        public Guid LastFailureId { get; private set; }

        public Task<DeadLetterDiscardResult> DiscardAsync(
            Guid failureId, string discardedBy, string discardReason, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastFailureId = failureId;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingReplayer : IDeadLetterReplayer
    {
        public List<Guid> SeenIds { get; } = new();

        public Task<DeadLetterReplayResult> ReplayAsync(
            Guid failureId, string replayedBy, CancellationToken cancellationToken = default)
        {
            SeenIds.Add(failureId);
            return Task.FromResult(new DeadLetterReplayResult(
                DeadLetterReplayOutcome.Success, Guid.NewGuid(), null, null));
        }
    }
}
