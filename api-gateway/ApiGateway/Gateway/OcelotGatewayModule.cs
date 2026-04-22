using Ocelot.DependencyInjection;
using Ocelot.Middleware;

namespace ApiGateway.Gateway;

public static class OcelotGatewayModule
{
    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: false);
        builder.Services.AddOcelot(builder.Configuration);
    }

    public static Task UseMiddlewareAsync(WebApplication app) => app.UseOcelot();
}
