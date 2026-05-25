namespace ApiGateway.Features.Operator.ReplayFailure;

internal static class ReplayFailureSliceExtensions
{
    public static IServiceCollection AddReplayFailureSlice(this IServiceCollection services)
    {
        services.AddScoped<ReplayFailureHandler>();
        return services;
    }
}
