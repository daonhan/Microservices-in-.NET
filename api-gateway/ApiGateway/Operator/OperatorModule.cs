using System.Security.Claims;
using ApiGateway.Operator.OutboxPolling;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
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

        group.MapGet("/", async (
            IDeadLetterStore store,
            string? service,
            string? eventType,
            DeadLetterStatus? status,
            DateTime? from,
            DateTime? to,
            DeadLetterOrigin? origin,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default) =>
        {
            var filter = new DeadLetterFilter(service, eventType, status, from, to, page, pageSize, origin);
            var result = await store.ListAsync(filter, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDeadLetterStore store,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            return await GetFailureDetail(id, store, configuration, cancellationToken);
        });

        group.MapPost("/{id:guid}/replay", async (
            Guid id,
            IDeadLetterReplayer replayer,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var replayedBy = user.FindFirstValue(JwtClaimTypes.Subject)
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.Identity?.Name
                ?? "unknown";

            var result = await replayer.ReplayAsync(id, replayedBy, cancellationToken);

            return result.Outcome switch
            {
                DeadLetterReplayOutcome.Success =>
                    Results.Accepted($"/operator/api/failures/{id}", new { id, newMessageId = result.NewMessageId }),
                DeadLetterReplayOutcome.NotFound =>
                    Results.NotFound(new { id, reason = result.FailureReason }),
                DeadLetterReplayOutcome.NotPending =>
                    Results.Conflict(new { id, reason = result.FailureReason }),
                _ =>
                    Results.Problem(
                        title: "Replay failed during publish",
                        detail: result.FailureReason,
                        statusCode: StatusCodes.Status502BadGateway)
            };
        });

        group.MapPost("/{id:guid}/discard", async (
            Guid id,
            [FromBody] DiscardRequest? request,
            IDeadLetterDiscarder discarder,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var discardedBy = user.FindFirstValue(JwtClaimTypes.Subject)
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.Identity?.Name
                ?? "unknown";

            return await DiscardFailure(id, request, discardedBy, discarder, cancellationToken);
        });

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

    public static async Task<IResult> GetFailureDetail(
        Guid id,
        IDeadLetterStore store,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var message = await store.GetAsync(id, cancellationToken);
        if (message is null)
        {
            return Results.NotFound();
        }

        var traceUiBaseUrl = configuration["Operator:TraceUiBaseUrl"];
        string? traceUrl = null;
        if (message.CorrelationId.HasValue && !string.IsNullOrWhiteSpace(traceUiBaseUrl))
        {
            traceUrl = traceUiBaseUrl.TrimEnd('/') + "/" + message.CorrelationId.Value;
        }

        return Results.Ok(new DeadLetterDetailResponse(message, traceUrl));
    }

    public static async Task<IResult> DiscardFailure(
        Guid id,
        DiscardRequest? request,
        string discardedBy,
        IDeadLetterDiscarder discarder,
        CancellationToken cancellationToken)
    {
        var reason = request?.Reason;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Results.BadRequest(new { id, error = "discard reason is required" });
        }

        var result = await discarder.DiscardAsync(id, discardedBy, reason, cancellationToken);

        return result.Outcome switch
        {
            DeadLetterDiscardOutcome.Success =>
                Results.Accepted($"/operator/api/failures/{id}", new { id, discardedBy, reason }),
            DeadLetterDiscardOutcome.NotFound =>
                Results.NotFound(new { id, reason = result.FailureReason }),
            DeadLetterDiscardOutcome.NotPending =>
                Results.Conflict(new { id, reason = result.FailureReason }),
            DeadLetterDiscardOutcome.ReasonRequired =>
                Results.BadRequest(new { id, error = "discard reason is required" }),
            _ =>
                Results.Problem(
                    title: "Discard failed",
                    detail: result.FailureReason,
                    statusCode: StatusCodes.Status500InternalServerError)
        };
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

public sealed record DeadLetterDetailResponse(DeadLetterMessage Message, string? TraceUrl);

public sealed record DiscardRequest(string Reason);

public sealed record BatchReplayRequest(IReadOnlyList<Guid> Ids);

public sealed record BatchReplayItem(Guid Id, string Status, Guid? NewMessageId, string? Reason);

public sealed record BatchReplayResponse(IReadOnlyList<BatchReplayItem> Items);

internal static class JwtClaimTypes
{
    public const string Subject = "sub";
}
