namespace ApiGateway.Features.Operator.DiscardFailure;

internal static class DiscardFailureSliceExtensions
{
    public static IServiceCollection AddDiscardFailureSlice(this IServiceCollection services)
    {
        services.AddScoped<DiscardFailureHandler>();
        return services;
    }
}
