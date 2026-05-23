namespace Payment.Service.Features.GetPaymentById;

internal static class GetPaymentByIdSliceExtensions
{
    public static IServiceCollection AddGetPaymentByIdSlice(this IServiceCollection services)
    {
        services.AddScoped<GetPaymentByIdHandler>();
        return services;
    }
}
