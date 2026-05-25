namespace ApiGateway.Features.Operator.GetFailureDetail;

internal static class GetFailureDetailEndpoint
{
    public static IEndpointRouteBuilder MapGetFailureDetailSlice(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{id:guid}", HandleAsync);
        return routeBuilder;
    }

    internal static Task<IResult> HandleAsync(
        Guid id,
        GetFailureDetailHandler handler,
        CancellationToken cancellationToken)
        => handler.HandleAsync(id, cancellationToken);
}
