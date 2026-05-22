using Auth.Service.Domain.Abstractions;

namespace Auth.Service.Infrastructure.Signing;

public static class SigningInfrastructureExtensions
{
    public static IServiceCollection AddSigningInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        var signingOptions = new SigningOptions();
        configuration.GetSection(SigningOptions.SectionName).Bind(signingOptions);
        services.AddSingleton(signingOptions);

        services.AddSingleton<IRsaKeyProvider, PemFileRsaKeyProvider>();

        return services;
    }
}
