using Microsoft.AspNetCore.Http.HttpResults;

namespace Saga.Service.Features.Operator.GetSaga;

internal static class GetSagaEndpoint
{
    public static async Task<Results<Ok<GetSagaResponse>, NotFound>> Handle(
        Guid id,
        GetSagaHandler handler,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(id, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }
}
