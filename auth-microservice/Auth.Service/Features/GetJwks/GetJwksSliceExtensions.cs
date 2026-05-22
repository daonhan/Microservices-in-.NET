namespace Auth.Service.Features.GetJwks;

internal static class GetJwksSliceExtensions
{
    public static IServiceCollection AddGetJwksSlice(this IServiceCollection services)
    {
        services.AddScoped<GetJwksHandler>();
        return services;
    }
}
