namespace ApiGateway.Features.Operator.GetFailureDetail;

internal static class GetFailureDetailSliceExtensions
{
    public static IServiceCollection AddGetFailureDetailSlice(this IServiceCollection services)
    {
        services.AddScoped<GetFailureDetailHandler>();
        return services;
    }
}
