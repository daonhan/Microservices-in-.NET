using ECommerce.Shared.Authentication;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.AspNetCore.Mvc;

namespace Order.Service.Endpoints;

public static class InternalOutboxEndpoints
{
    public static void RegisterInternalOutboxEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/internal/outbox/failed",
            async ([FromServices] IOutboxStore outboxStore) =>
            {
                var failed = await outboxStore.GetFailedOutboxEvents();

                return TypedResults.Ok(failed.Select(e => new
                {
                    id = e.Id,
                    eventType = e.EventType,
                    data = e.Data,
                    attempts = e.Attempts,
                    lastError = e.LastError,
                    lastAttemptAt = e.LastAttemptAt,
                    correlationId = e.CorrelationId,
                }));
            })
            .RequireAuthorization(AuthorizationPolicies.RequireServicePolicy);
    }
}
