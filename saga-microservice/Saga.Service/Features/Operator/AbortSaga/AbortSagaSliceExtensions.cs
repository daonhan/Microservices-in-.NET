using ECommerce.Shared.Authentication;

namespace Saga.Service.Features.Operator.AbortSaga;

internal static class AbortSagaSliceExtensions
{
    public static IServiceCollection AddAbortSagaSlice(this IServiceCollection services)
    {
        services.AddScoped<AbortSagaHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapAbortSagaSlice(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/operator/api/sagas/{id:guid}/abort", AbortSagaEndpoint.Handle)
            .RequireAuthorization(AuthorizationPolicies.RequireServicePolicy)
            .WithName("AbortOperatorSaga")
            .WithSummary("Force a running saga into compensation.")
            .WithTags("Operator Sagas")
            .Produces<AbortSagaResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        return builder;
    }
}
