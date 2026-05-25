using System.Security.Claims;
using ApiGateway.Infrastructure.Auth;
using ApiGateway.Infrastructure.Polling;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Operator;

public static class OperatorModule
{
    public const string OperatorPathPrefix = "/operator";

    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddPlatformEventBus(builder.Configuration);
        builder.Services.AddDeadLetter(builder.Configuration);
        builder.Services.AddRequireOperatorPolicy();

        var pollerOptions = new OutboxPollerOptions();
        builder.Configuration.GetSection(OutboxPollerOptions.SectionName).Bind(pollerOptions);
        builder.Services.AddSingleton(pollerOptions);

        if (pollerOptions.Enabled)
        {
            builder.Services.AddHttpClient<IOutboxFailureClient, OutboxFailureClient>();
            builder.Services.AddHostedService<OutboxFailurePoller>();
        }
    }

    public static void MapEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/operator/api/failures")
            .RequireAuthorization(AuthorizationPolicies.RequireOperatorPolicy);

        group.MapPost("/replay-batch", async (
            [FromBody] BatchReplayRequest? request,
            IDeadLetterReplayer replayer,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var replayedBy = user.FindFirstValue(JwtClaimTypes.Subject)
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.Identity?.Name
                ?? "unknown";

            return await BatchReplay(request, replayedBy, replayer, cancellationToken);
        });
    }

    public static async Task<IResult> BatchReplay(
        BatchReplayRequest? request,
        string replayedBy,
        IDeadLetterReplayer replayer,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Ids is null || request.Ids.Count == 0)
        {
            return Results.BadRequest(new { error = "ids are required" });
        }

        var items = new List<BatchReplayItem>(request.Ids.Count);
        foreach (var id in request.Ids)
        {
            var result = await replayer.ReplayAsync(id, replayedBy, cancellationToken);
            var status = result.Outcome switch
            {
                DeadLetterReplayOutcome.Success => "success",
                DeadLetterReplayOutcome.NotFound => "not_found",
                DeadLetterReplayOutcome.NotPending => "not_pending",
                DeadLetterReplayOutcome.PublishFailed => "publish_failed",
                _ => "unknown"
            };
            items.Add(new BatchReplayItem(id, status, result.NewMessageId, result.FailureReason));
        }

        return Results.Ok(new BatchReplayResponse(items));
    }
}

public sealed record BatchReplayRequest(IReadOnlyList<Guid> Ids);

public sealed record BatchReplayItem(Guid Id, string Status, Guid? NewMessageId, string? Reason);

public sealed record BatchReplayResponse(IReadOnlyList<BatchReplayItem> Items);
