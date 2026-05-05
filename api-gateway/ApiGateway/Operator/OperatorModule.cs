using System.Security.Claims;
using ApiGateway.Operator.OutboxPolling;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using ECommerce.Shared.Infrastructure.RabbitMq;

namespace ApiGateway.Operator;

public static class OperatorModule
{
    public const string OperatorPathPrefix = "/operator";

    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddRabbitMqEventBus(builder.Configuration);
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
    }
}

internal static class JwtClaimTypes
{
    public const string Subject = "sub";
}
