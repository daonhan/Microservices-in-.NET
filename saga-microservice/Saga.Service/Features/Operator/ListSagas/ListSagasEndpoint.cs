using Microsoft.AspNetCore.Http.HttpResults;
using Saga.Service.Domain;

namespace Saga.Service.Features.Operator.ListSagas;

internal static class ListSagasEndpoint
{
    public static async Task<Ok<ListSagasResponse>> Handle(
        ListSagasHandler handler,
        string? type,
        SagaStatus? status,
        bool? overdue,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(type, status, overdue, cancellationToken);
        return TypedResults.Ok(response);
    }
}
