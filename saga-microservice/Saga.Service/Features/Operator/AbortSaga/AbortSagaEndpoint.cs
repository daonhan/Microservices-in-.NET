namespace Saga.Service.Features.Operator.AbortSaga;

internal static class AbortSagaEndpoint
{
    public static Task<IResult> Handle(
        Guid id,
        AbortSagaHandler handler,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(id, cancellationToken);
}
