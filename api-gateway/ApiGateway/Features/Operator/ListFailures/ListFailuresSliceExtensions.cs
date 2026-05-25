namespace ApiGateway.Features.Operator.ListFailures;

internal static class ListFailuresSliceExtensions
{
    public static IServiceCollection AddListFailuresSlice(this IServiceCollection services)
    {
        services.AddScoped<ListFailuresHandler>();
        return services;
    }
}
