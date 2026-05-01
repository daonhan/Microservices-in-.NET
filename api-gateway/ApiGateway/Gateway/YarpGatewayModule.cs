using ApiGateway.Gateway.SwaggerAggregation;

namespace ApiGateway.Gateway;

public static class YarpGatewayModule
{
    public const string AdminOnlyPolicy = "AdminOnly";
    private const string AdministratorRole = "Administrator";
    private const string RoleClaimType = "user_role";
    private static readonly IReadOnlyDictionary<string, string> ClusterAddressKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Auth"] = "auth-cluster",
        ["Product"] = "product-cluster",
        ["Basket"] = "basket-cluster",
        ["Order"] = "order-cluster",
        ["Inventory"] = "inventory-cluster",
        ["Shipping"] = "shipping-cluster",
        ["Payment"] = "payment-cluster"
    };

    public static void AddServices(WebApplicationBuilder builder)
    {
        ApplyClusterAddressOverrides(builder.Configuration);

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

    private static void ApplyClusterAddressOverrides(ConfigurationManager configuration)
    {
        foreach (var (addressKey, clusterId) in ClusterAddressKeys)
        {
            var address = configuration[$"Gateway:ClusterAddresses:{addressKey}"];
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            configuration[$"ReverseProxy:Clusters:{clusterId}:Destinations:default:Address"] = address;
        }
    }
}
