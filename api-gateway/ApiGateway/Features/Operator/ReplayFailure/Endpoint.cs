using System.Security.Claims;

namespace ApiGateway.Features.Operator.ReplayFailure;

internal static class ReplayFailureEndpoint
{
    public static IEndpointRouteBuilder MapReplayFailureSlice(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{id:guid}/replay", HandleAsync);
        return routeBuilder;
    }

    internal static Task<IResult> HandleAsync(
        Guid id,
        ReplayFailureHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
        => handler.HandleAsync(id, user, cancellationToken);
}
