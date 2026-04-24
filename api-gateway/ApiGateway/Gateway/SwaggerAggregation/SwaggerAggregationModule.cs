using System.Text;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Gateway.SwaggerAggregation;

public static class SwaggerAggregationModule
{
    private const string SpecProxyRoutePrefix = "swagger-";

    private sealed record SpecProxyTarget(string ServiceTag, string ServiceDisplayName, string ClusterId, string SpecPath);

    private static readonly IReadOnlyDictionary<string, SpecProxyTarget> SpecProxyRoutes =
        new Dictionary<string, SpecProxyTarget>(StringComparer.Ordinal)
        {
            ["swagger-auth-spec"] = new("auth", "Auth", "auth-cluster", "/swagger/auth/v1/swagger.json"),
            ["swagger-product-spec"] = new("product", "Product", "product-cluster", "/swagger/product/v1/swagger.json"),
        };

    public static IReverseProxyBuilder AddSwaggerAggregation(this IReverseProxyBuilder proxy) =>
        proxy.AddTransforms(context =>
        {
            if (!SpecProxyRoutes.TryGetValue(context.Route.RouteId, out var target))
            {
                return;
            }

            context.AddResponseTransform(async transformContext =>
            {
                var response = transformContext.ProxyResponse;
                if (response is null || !response.IsSuccessStatusCode)
                {
                    return;
                }

                var services = transformContext.HttpContext.RequestServices;
                var configProvider = services.GetRequiredService<IProxyConfigProvider>();
                var allRoutes = GatewayRouteDiscovery.DiscoverRoutes(configProvider);
                var clusterRoutes = allRoutes
                    .Where(r => r.ClusterId == target.ClusterId
                        && !r.RouteId.StartsWith(SpecProxyRoutePrefix, StringComparison.Ordinal))
                    .ToList();

                var raw = await response.Content.ReadAsStringAsync();
                var transformed = GatewaySpecTransformer.Transform(raw, target.ServiceTag, clusterRoutes);
                var bytes = Encoding.UTF8.GetBytes(transformed);

                transformContext.SuppressResponseBody = true;
                transformContext.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                transformContext.HttpContext.Response.ContentLength = bytes.Length;
                await transformContext.HttpContext.Response.Body.WriteAsync(bytes);
            });
        });

    public static void UseSwaggerAggregation(this WebApplication app)
    {
        var isDevOrStaging = app.Environment.IsDevelopment() || app.Environment.IsStaging();
        if (!isDevOrStaging)
        {
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path.StartsWithSegments("/swagger"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next();
            });
            return;
        }

        app.UseSwaggerUI(options =>
        {
            foreach (var target in SpecProxyRoutes.Values)
            {
                options.SwaggerEndpoint(target.SpecPath, target.ServiceDisplayName);
            }
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "API Gateway — Combined Swagger UI";
        });
    }
}
