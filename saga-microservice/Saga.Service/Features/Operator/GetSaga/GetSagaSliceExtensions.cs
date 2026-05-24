using ECommerce.Shared.Authentication;

namespace Saga.Service.Features.Operator.GetSaga;

internal static class GetSagaSliceExtensions
{
    public static IServiceCollection AddGetSagaSlice(this IServiceCollection services)
    {
        services.AddScoped<GetSagaHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapGetSagaSlice(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/operator/api/sagas/{id:guid}", GetSagaEndpoint.Handle)
            .RequireAuthorization(AuthorizationPolicies.RequireServicePolicy)
            .WithName("GetOperatorSagaDetail")
            .WithSummary("Get saga detail and transition history.")
            .WithTags("Operator Sagas")
            .Produces<GetSagaResponse>()
            .Produces(StatusCodes.Status404NotFound);
        return builder;
    }
}
