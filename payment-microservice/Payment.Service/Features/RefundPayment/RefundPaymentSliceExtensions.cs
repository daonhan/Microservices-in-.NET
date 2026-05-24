using Payment.Service.Infrastructure.Outbox;

namespace Payment.Service.Features.RefundPayment;

internal static class RefundPaymentSliceExtensions
{
    public static IServiceCollection AddRefundPaymentSlice(this IServiceCollection services)
    {
        services.AddScoped<RefundPaymentHandler>();
        services.AddScoped<IIntegrationMap, PaymentRefundedIntegrationMap>();
        return services;
    }
}
