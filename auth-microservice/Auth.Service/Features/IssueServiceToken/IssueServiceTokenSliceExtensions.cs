using Auth.Service.Domain.Tokens;

namespace Auth.Service.Features.IssueServiceToken;

internal static class IssueServiceTokenSliceExtensions
{
    public static IServiceCollection AddIssueServiceTokenSlice(this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceClientOptions = new ServiceClientOptions();
        configuration.GetSection(ServiceClientOptions.SectionName).Bind(serviceClientOptions);
        services.AddSingleton(serviceClientOptions);

        services.AddScoped<IServiceTokenService, ServiceTokenService>();
        services.AddScoped<IssueServiceTokenHandler>();

        return services;
    }
}
