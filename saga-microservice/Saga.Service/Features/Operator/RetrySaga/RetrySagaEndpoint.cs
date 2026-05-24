namespace Saga.Service.Features.Operator.RetrySaga;

internal static class RetrySagaEndpoint
{
    public static Task<IResult> Handle(
        Guid id,
        RetrySagaHandler handler,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(id, cancellationToken);
}
