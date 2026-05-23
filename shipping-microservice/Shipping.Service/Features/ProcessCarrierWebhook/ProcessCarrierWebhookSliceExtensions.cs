namespace Shipping.Service.Features.ProcessCarrierWebhook;

internal static class ProcessCarrierWebhookSliceExtensions
{
    public static IServiceCollection AddProcessCarrierWebhookSlice(this IServiceCollection services)
    {
        services.AddScoped<ProcessCarrierWebhookHandler>();
        return services;
    }
}
