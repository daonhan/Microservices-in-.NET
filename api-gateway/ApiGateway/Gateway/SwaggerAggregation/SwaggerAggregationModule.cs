using System.Text;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Gateway.SwaggerAggregation;

public static class SwaggerAggregationModule
{
    private const string AuthSpecRouteId = "swagger-auth-spec";
    private const string AuthSpecPath = "/swagger/auth/v1/swagger.json";

    public static IReverseProxyBuilder AddSwaggerAggregation(this IReverseProxyBuilder proxy) =>
        proxy.AddTransforms(context =>
        {
            var serviceTag = context.Route.RouteId switch
            {
                AuthSpecRouteId => "auth",
                _ => null
            };

            if (serviceTag is null)
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

                var raw = await response.Content.ReadAsStringAsync();
                var transformed = GatewaySpecTransformer.Transform(raw, serviceTag);
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
            options.SwaggerEndpoint(AuthSpecPath, "Auth");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "API Gateway — Combined Swagger UI";
        });
    }
}
