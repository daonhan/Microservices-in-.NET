using Auth.Service.Services.Signing;
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

        var signingOptions = new SigningOptions();
        configuration.GetSection(SigningOptions.SectionName).Bind(signingOptions);
        services.AddSingleton(signingOptions);
        services.AddSingleton<IRsaKeyProvider, PemFileRsaKeyProvider>();

        services.AddScoped<ITokenService, JwtTokenService>();
    }
}
