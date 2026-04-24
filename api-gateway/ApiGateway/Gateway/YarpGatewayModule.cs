using ApiGateway.Gateway.SwaggerAggregation;

namespace ApiGateway.Gateway;

public static class YarpGatewayModule
{
    public const string AdminOnlyPolicy = "AdminOnly";
    private const string AdministratorRole = "Administrator";
    private const string RoleClaimType = "user_role";

    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
            .AddSwaggerAggregation();

        builder.Services.AddAuthorization(options =>
            options.AddPolicy(AdminOnlyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(RoleClaimType, AdministratorRole)));
    }

    public static void UseMiddleware(WebApplication app)
    {
        app.UseSwaggerAggregation();
        app.MapReverseProxy();
    }
}
