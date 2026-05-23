using Payment.Service.Infrastructure.Outbox;

namespace Payment.Service.Features.CapturePayment;

internal static class CapturePaymentSliceExtensions
{
    public static IServiceCollection AddCapturePaymentSlice(this IServiceCollection services)
    {
        services.AddScoped<CapturePaymentHandler>();
        services.AddScoped<IIntegrationMap, PaymentCapturedIntegrationMap>();
        return services;
    }
}
