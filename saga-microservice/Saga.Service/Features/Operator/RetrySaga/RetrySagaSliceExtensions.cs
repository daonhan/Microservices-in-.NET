using ECommerce.Shared.Authentication;

namespace Saga.Service.Features.Operator.RetrySaga;

internal static class RetrySagaSliceExtensions
{
    public static IServiceCollection AddRetrySagaSlice(this IServiceCollection services)
    {
        services.AddScoped<RetrySagaHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapRetrySagaSlice(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/operator/api/sagas/{id:guid}/retry", RetrySagaEndpoint.Handle)
            .RequireAuthorization(AuthorizationPolicies.RequireServicePolicy)
            .WithName("RetryOperatorSaga")
            .WithSummary("Requeue the in-flight command for a saga.")
            .WithTags("Operator Sagas")
            .Produces<RetrySagaResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        return builder;
    }
}
