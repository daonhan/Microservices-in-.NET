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

    public static Task UseMiddlewareAsync(WebApplication app)
    {
        // Ocelot's middleware is terminal; branch so /health/* and /metrics
        // fall through to the endpoints mapped by MapPlatformHealthChecks and
        // UsePrometheusExporter rather than being swallowed by Ocelot's router.
        app.MapWhen(
            ctx => !ctx.Request.Path.StartsWithSegments("/health")
                && !ctx.Request.Path.StartsWithSegments("/metrics"),
            branch => branch.UseOcelot().GetAwaiter().GetResult());

        return Task.CompletedTask;
    }
}
