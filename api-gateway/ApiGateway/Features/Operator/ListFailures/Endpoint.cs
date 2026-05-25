using ECommerce.Shared.Infrastructure.DeadLetter.Models;

namespace ApiGateway.Features.Operator.ListFailures;

internal static class ListFailuresEndpoint
{
    public static IEndpointRouteBuilder MapListFailuresSlice(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/", HandleAsync);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        ListFailuresHandler handler,
        string? service,
        string? eventType,
        DeadLetterStatus? status,
        DateTime? from,
        DateTime? to,
        DeadLetterOrigin? origin,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(service, eventType, status, from, to, origin, page, pageSize, cancellationToken);
        return Results.Ok(result);
    }
}
