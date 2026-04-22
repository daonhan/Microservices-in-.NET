namespace ApiGateway.Gateway;

public static class YarpGatewayModule
{
    public static void AddServices(WebApplicationBuilder builder) =>
        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

    public static void UseMiddleware(WebApplication app) =>
        app.MapReverseProxy();
}
