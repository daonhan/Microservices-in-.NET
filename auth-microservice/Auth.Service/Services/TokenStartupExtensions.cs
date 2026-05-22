using Auth.Service.Domain.Tokens;
using ECommerce.Shared.Authentication;

namespace Auth.Service.Services;

public static class TokenStartupExtensions
{
    public static void RegisterTokenService(this IServiceCollection services,
        IConfigurationManager configuration)
    {
        var authOptions = new AuthOptions();
        configuration.GetSection(AuthOptions.AuthenticationSectionName).Bind(authOptions);
        services.AddSingleton(authOptions);

        services.AddScoped<JwtTokenService>();
        services.AddScoped<LoginHandler>();

        var serviceClientOptions = new ServiceClientOptions();
        configuration.GetSection(ServiceClientOptions.SectionName).Bind(serviceClientOptions);
        services.AddSingleton(serviceClientOptions);
        services.AddScoped<IServiceTokenService, ServiceTokenService>();
    }
}
