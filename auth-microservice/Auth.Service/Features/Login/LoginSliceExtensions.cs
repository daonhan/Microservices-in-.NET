using Auth.Service.Domain.Tokens;

namespace Auth.Service.Features.Login;

internal static class LoginSliceExtensions
{
    public static IServiceCollection AddLoginSlice(this IServiceCollection services)
    {
        services.AddScoped<JwtTokenService>();
        services.AddScoped<LoginHandler>();

        return services;
    }
}
