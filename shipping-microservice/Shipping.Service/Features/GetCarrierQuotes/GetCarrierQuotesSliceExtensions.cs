namespace Shipping.Service.Features.GetCarrierQuotes;

internal static class GetCarrierQuotesSliceExtensions
{
    public static IServiceCollection AddGetCarrierQuotesSlice(this IServiceCollection services)
    {
        services.AddScoped<GetCarrierQuotesHandler>();
        return services;
    }
}
