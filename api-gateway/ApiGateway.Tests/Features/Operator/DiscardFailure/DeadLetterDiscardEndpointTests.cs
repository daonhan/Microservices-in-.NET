using System.Security.Claims;
using ApiGateway.Features.Operator.DiscardFailure;
using ApiGateway.Infrastructure.Auth;
using ECommerce.Shared.Infrastructure.DeadLetter;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Tests.Features.Operator.DiscardFailure;

public sealed class DeadLetterDiscardEndpointTests
{
    private static readonly Guid SampleId = Guid.NewGuid();

    private static int StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 0;

    private static ClaimsPrincipal AsUser(string sub) =>
        new(new ClaimsIdentity([new Claim(JwtClaimTypes.Subject, sub)]));

    [Fact]
    public async Task Given_null_request_When_DiscardFailure_Then_returns_BadRequest_and_does_not_call_discarder()
    {
        var discarder = new FakeDiscarder();
        var handler = new DiscardFailureHandler(discarder);

        var result = await handler.HandleAsync(SampleId, request: null, AsUser("alice"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, discarder.CallCount);
    }

    [Fact]
    public async Task Given_blank_reason_When_DiscardFailure_Then_returns_BadRequest_and_does_not_call_discarder()
    {
        var discarder = new FakeDiscarder();
        var handler = new DiscardFailureHandler(discarder);

        var result = await handler.HandleAsync(SampleId, new DiscardRequest("   "), AsUser("alice"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, discarder.CallCount);
    }

    [Fact]
    public async Task Given_successful_discard_When_DiscardFailure_Then_returns_Accepted_and_calls_discarder_with_reason()
    {
        var discarder = new FakeDiscarder
        {
            Result = new DeadLetterDiscardResult(DeadLetterDiscardOutcome.Success, null, null)
        };
        var handler = new DiscardFailureHandler(discarder);

        var result = await handler.HandleAsync(SampleId, new DiscardRequest("duplicate event"), AsUser("alice"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status202Accepted, StatusOf(result));
        Assert.Equal(1, discarder.CallCount);
        Assert.Equal(SampleId, discarder.LastFailureId);
        Assert.Equal("alice", discarder.LastDiscardedBy);
        Assert.Equal("duplicate event", discarder.LastReason);
    }

    [Fact]
    public async Task Given_unknown_id_When_DiscardFailure_Then_returns_NotFound()
    {
        var discarder = new FakeDiscarder
        {
            Result = new DeadLetterDiscardResult(DeadLetterDiscardOutcome.NotFound, "not_found", null)
        };
        var handler = new DiscardFailureHandler(discarder);

        var result = await handler.HandleAsync(SampleId, new DiscardRequest("duplicate event"), AsUser("alice"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Given_already_transitioned_message_When_DiscardFailure_Then_returns_Conflict()
    {
        var discarder = new FakeDiscarder
        {
            Result = new DeadLetterDiscardResult(DeadLetterDiscardOutcome.NotPending, "status_Replayed", null)
        };
        var handler = new DiscardFailureHandler(discarder);

        var result = await handler.HandleAsync(SampleId, new DiscardRequest("duplicate event"), AsUser("alice"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    private sealed class FakeDiscarder : IDeadLetterDiscarder
    {
        public DeadLetterDiscardResult Result { get; set; } =
            new DeadLetterDiscardResult(DeadLetterDiscardOutcome.Success, null, null);

        public int CallCount { get; private set; }

        public Guid LastFailureId { get; private set; }

        public string? LastDiscardedBy { get; private set; }

        public string? LastReason { get; private set; }

        public Task<DeadLetterDiscardResult> DiscardAsync(Guid failureId, string discardedBy, string discardReason, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastFailureId = failureId;
            LastDiscardedBy = discardedBy;
            LastReason = discardReason;
            return Task.FromResult(Result);
        }
    }
}
