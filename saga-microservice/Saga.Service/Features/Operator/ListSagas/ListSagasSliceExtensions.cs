using ECommerce.Shared.Authentication;

namespace Saga.Service.Features.Operator.ListSagas;

internal static class ListSagasSliceExtensions
{
    public static IServiceCollection AddListSagasSlice(this IServiceCollection services)
    {
        services.AddScoped<ListSagasHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapListSagasSlice(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/operator/api/sagas", ListSagasEndpoint.Handle)
            .RequireAuthorization(AuthorizationPolicies.RequireServicePolicy)
            .WithName("ListOperatorSagas")
            .WithSummary("List saga instances for operator workflows.")
            .WithTags("Operator Sagas")
            .Produces<ListSagasResponse>();
        return builder;
    }
}
