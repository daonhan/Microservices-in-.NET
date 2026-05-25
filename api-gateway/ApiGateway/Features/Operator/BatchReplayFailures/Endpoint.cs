using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Features.Operator.BatchReplayFailures;

internal static class BatchReplayFailuresEndpoint
{
    public static IEndpointRouteBuilder MapBatchReplayFailuresSlice(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/replay-batch", HandleAsync);
        return routeBuilder;
    }

    internal static Task<IResult> HandleAsync(
        [FromBody] BatchReplayRequest? request,
        BatchReplayFailuresHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
        => handler.HandleAsync(request, user, cancellationToken);
}
