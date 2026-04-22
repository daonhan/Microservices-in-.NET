using Microsoft.Extensions.Options;

namespace ApiGateway.Gateway;

public static class GatewayProviderExtensions
{
    private static readonly Action<ILogger, GatewayProvider, Exception?> LogProviderActive =
        LoggerMessage.Define<GatewayProvider>(
            LogLevel.Information,
            new EventId(1, "GatewayProviderActive"),
            "ApiGateway starting with provider={Provider}");

    public static WebApplicationBuilder AddConfiguredGateway(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<GatewayProviderOptions>()
            .BindConfiguration(GatewayProviderOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var providerValue = builder.Configuration[$"{GatewayProviderOptions.SectionName}:Provider"];

        if (!Enum.TryParse<GatewayProvider>(providerValue, ignoreCase: true, out var provider))
        {
            var valid = string.Join(", ", Enum.GetNames<GatewayProvider>());
            throw new InvalidOperationException(
                $"Unknown gateway provider '{providerValue}'. Valid values: {valid}.");
        }

        switch (provider)
        {
            case GatewayProvider.Ocelot:
                OcelotGatewayModule.AddServices(builder);
                break;
            case GatewayProvider.Yarp:
                YarpGatewayModule.AddServices(builder);
                break;
            default:
                throw new InvalidOperationException($"Unhandled gateway provider '{provider}'.");
        }

        return builder;
    }

    public static async Task UseConfiguredGatewayAsync(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<GatewayProviderOptions>>().Value;
        var provider = Enum.Parse<GatewayProvider>(options.Provider, ignoreCase: true);

        LogProviderActive(app.Logger, provider, null);

        switch (provider)
        {
            case GatewayProvider.Ocelot:
                await OcelotGatewayModule.UseMiddlewareAsync(app);
                break;
            case GatewayProvider.Yarp:
                YarpGatewayModule.UseMiddleware(app);
                break;
            default:
                throw new InvalidOperationException($"Unhandled gateway provider '{provider}'.");
        }
    }
}
