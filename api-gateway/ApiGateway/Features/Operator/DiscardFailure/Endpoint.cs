using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Features.Operator.DiscardFailure;

internal static class DiscardFailureEndpoint
{
    public static IEndpointRouteBuilder MapDiscardFailureSlice(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{id:guid}/discard", HandleAsync);
        return routeBuilder;
    }

    internal static Task<IResult> HandleAsync(
        Guid id,
        [FromBody] DiscardRequest? request,
        DiscardFailureHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
        => handler.HandleAsync(id, request, user, cancellationToken);
}
