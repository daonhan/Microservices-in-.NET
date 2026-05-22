namespace Auth.Service.Features.GetOpenIdConfiguration;

internal static class GetOpenIdConfigurationSliceExtensions
{
    public static IServiceCollection AddGetOpenIdConfigurationSlice(this IServiceCollection services)
    {
        services.AddScoped<GetOpenIdConfigurationHandler>();
        return services;
    }
}
