namespace Payment.Service.Features.GetPaymentByOrder;

internal static class GetPaymentByOrderSliceExtensions
{
    public static IServiceCollection AddGetPaymentByOrderSlice(this IServiceCollection services)
    {
        services.AddScoped<GetPaymentByOrderHandler>();
        return services;
    }
}
