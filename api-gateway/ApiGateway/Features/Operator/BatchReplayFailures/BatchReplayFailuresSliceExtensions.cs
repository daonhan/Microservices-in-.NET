namespace ApiGateway.Features.Operator.BatchReplayFailures;

internal static class BatchReplayFailuresSliceExtensions
{
    public static IServiceCollection AddBatchReplayFailuresSlice(this IServiceCollection services)
    {
        services.AddScoped<BatchReplayFailuresHandler>();
        return services;
    }
}
