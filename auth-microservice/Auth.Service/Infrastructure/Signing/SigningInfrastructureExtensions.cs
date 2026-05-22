using Auth.Service.Domain.Abstractions;
using ECommerce.Shared.Authentication;

namespace Auth.Service.Infrastructure.Signing;

public static class SigningInfrastructureExtensions
{
    public static IServiceCollection AddSigningInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        var signingOptions = new SigningOptions();
        configuration.GetSection(SigningOptions.SectionName).Bind(signingOptions);
        services.AddSingleton(signingOptions);

        var authOptions = new AuthOptions();
        configuration.GetSection(AuthOptions.AuthenticationSectionName).Bind(authOptions);
        services.AddSingleton(authOptions);

        services.AddSingleton<IRsaKeyProvider, PemFileRsaKeyProvider>();

        return services;
    }
}
