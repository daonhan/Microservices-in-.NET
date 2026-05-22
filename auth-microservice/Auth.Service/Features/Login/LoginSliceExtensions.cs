using Auth.Service.Domain.Tokens;
using ECommerce.Shared.Authentication;

namespace Auth.Service.Features.Login;

internal static class LoginSliceExtensions
{
    public static IServiceCollection AddLoginSlice(this IServiceCollection services,
        IConfiguration configuration)
    {
        var authOptions = new AuthOptions();
        configuration.GetSection(AuthOptions.AuthenticationSectionName).Bind(authOptions);
        services.AddSingleton(authOptions);

        services.AddScoped<JwtTokenService>();
        services.AddScoped<LoginHandler>();

        return services;
    }
}
