using System.Text.Json.Nodes;
using ApiGateway.Gateway.SwaggerAggregation;

namespace ApiGateway.Tests.Gateway.SwaggerAggregation;

public class GatewaySpecTransformerTests
{
    private static GatewayRouteInfo Route(
        string routeId,
        string gatewayPath,
        string internalPath,
        string policy,
        params string[] methods) =>
        new(
            RouteId: routeId,
            GatewayPathPattern: gatewayPath,
            Methods: methods,
            InternalPathPattern: internalPath,
            AuthorizationPolicy: policy,
            ClusterId: "product-cluster");

    private static readonly IReadOnlyList<GatewayRouteInfo> ProductRoutes = new[]
    {
        Route("product-read", "/product/{id}", "/{id}", "Anonymous", "GET"),
        Route("product-write", "/product/{**rest}", "/{**rest}", "AdminOnly", "POST", "PUT"),
    };

    private const string ProductRawSpec = """
    {
      "openapi": "3.0.1",
      "info": { "title": "Product", "version": "v1" },
      "servers": [ { "url": "http://product-clusterip-service:8080" } ],
      "paths": {
        "/{productId}": {
          "get": { "responses": { "200": { "description": "ok" } } },
          "put": { "responses": { "204": { "description": "nc" } } }
        },
        "/": {
          "post": { "responses": { "201": { "description": "created" } } }
        },
        "/internal/admin": {
          "get": { "responses": { "200": { "description": "ok" } } }
        }
      }
    }
    """;

    [Fact]
    public void Transform_RewritesServersToGatewayOrigin()
    {
        var result = GatewaySpecTransformer.Transform(ProductRawSpec, "product", ProductRoutes);

        var doc = JsonNode.Parse(result)!.AsObject();
        var servers = doc["servers"]!.AsArray();
        Assert.Single(servers);
        Assert.Equal("/", servers[0]!["url"]!.GetValue<string>());
    }

    [Fact]
    public void Transform_RewritesInternalPathToGatewayFacingPath()
    {
        var result = GatewaySpecTransformer.Transform(ProductRawSpec, "product", ProductRoutes);

        var paths = JsonNode.Parse(result)!.AsObject()["paths"]!.AsObject();
        Assert.True(paths.ContainsKey("/product/{id}"));
        Assert.True(paths["/product/{id}"]!.AsObject().ContainsKey("get"));
    }

    [Fact]
    public void Transform_RewritesCatchallRouteToGatewayFacingPath()
    {
        var result = GatewaySpecTransformer.Transform(ProductRawSpec, "product", ProductRoutes);

        var paths = JsonNode.Parse(result)!.AsObject()["paths"]!.AsObject();
        // Root POST / → gateway /product (catchall with no remainder)
        Assert.True(paths.ContainsKey("/product"));
        Assert.True(paths["/product"]!.AsObject().ContainsKey("post"));
        // Param PUT /{productId} → gateway /product/{productId} (via catchall)
        Assert.True(paths.ContainsKey("/product/{productId}"));
        Assert.True(paths["/product/{productId}"]!.AsObject().ContainsKey("put"));
    }

    [Fact]
    public void Transform_DropsOperationsNotRoutedByGateway()
    {
        var result = GatewaySpecTransformer.Transform(ProductRawSpec, "product", ProductRoutes);

        var paths = JsonNode.Parse(result)!.AsObject()["paths"]!.AsObject();
        Assert.False(paths.ContainsKey("/internal/admin"));
        Assert.False(paths.ContainsKey("/product/internal/admin"));
    }

    [Fact]
    public void Transform_AppliesServiceTagToEveryOperation()
    {
        var result = GatewaySpecTransformer.Transform(ProductRawSpec, "product", ProductRoutes);

        var paths = JsonNode.Parse(result)!.AsObject()["paths"]!.AsObject();
        foreach (var pathEntry in paths)
        {
            foreach (var opEntry in pathEntry.Value!.AsObject())
            {
                var tags = opEntry.Value!.AsObject()["tags"]!.AsArray();
                Assert.Single(tags);
                Assert.Equal("product", tags[0]!.GetValue<string>());
            }
        }
    }

    [Fact]
    public void Transform_AnonymousPolicy_SetsEmptySecurity()
    {
        var result = GatewaySpecTransformer.Transform(ProductRawSpec, "product", ProductRoutes);

        var op = JsonNode.Parse(result)!.AsObject()["paths"]!.AsObject()
            ["/product/{id}"]!.AsObject()["get"]!.AsObject();
        var security = op["security"]!.AsArray();
        Assert.Empty(security);
    }

    [Fact]
    public void Transform_DefaultPolicy_SetsBearerRequirement()
    {
        var routes = new[]
        {
            Route("basket", "/basket/{**rest}", "/{**rest}", "Default", "GET", "POST"),
        };
        var raw = """
        { "paths": { "/user-1": { "get": { "responses": {} } } } }
        """;

        var result = GatewaySpecTransformer.Transform(raw, "basket", routes);

        var op = JsonNode.Parse(result)!.AsObject()["paths"]!.AsObject()
            ["/basket/user-1"]!.AsObject()["get"]!.AsObject();
        var security = op["security"]!.AsArray();
        Assert.Single(security);
        Assert.Contains("Bearer", security[0]!.AsObject().Select(kv => kv.Key));
    }

    [Fact]
    public void Transform_AdminOnlyPolicy_SetsBearerAndAppendsAdminClaimNote()
    {
        var result = GatewaySpecTransformer.Transform(ProductRawSpec, "product", ProductRoutes);

        var op = JsonNode.Parse(result)!.AsObject()["paths"]!.AsObject()
            ["/product"]!.AsObject()["post"]!.AsObject();
        var security = op["security"]!.AsArray();
        Assert.Single(security);
        Assert.Contains("Bearer", security[0]!.AsObject().Select(kv => kv.Key));
        var description = op["description"]!.GetValue<string>();
        Assert.Contains("Administrator", description, StringComparison.Ordinal);
        Assert.Contains("user_role", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Transform_DropsGlobalSecurityRequirement()
    {
        var raw = """
        {
          "paths": {},
          "security": [ { "Bearer": [] } ]
        }
        """;

        var result = GatewaySpecTransformer.Transform(raw, "auth", Array.Empty<GatewayRouteInfo>());

        var doc = JsonNode.Parse(result)!.AsObject();
        Assert.False(doc.ContainsKey("security"));
    }

    [Fact]
    public void Transform_MethodSpecificPolicies_OnSharedPath_RouteIndependently()
    {
        var routes = new[]
        {
            Route("inventory-read", "/inventory/{**rest}", "/{**rest}", "Default", "GET"),
            Route("inventory-write", "/inventory/{**rest}", "/{**rest}", "AdminOnly", "POST", "PUT"),
        };
        var raw = """
        {
          "paths": {
            "/42": {
              "get": { "responses": {} },
              "post": { "responses": {} }
            }
          }
        }
        """;

        var result = GatewaySpecTransformer.Transform(raw, "inventory", routes);

        var pathItem = JsonNode.Parse(result)!.AsObject()["paths"]!.AsObject()
            ["/inventory/42"]!.AsObject();
        var getSecurity = pathItem["get"]!.AsObject()["security"]!.AsArray();
        Assert.Single(getSecurity);
        Assert.Contains("Bearer", getSecurity[0]!.AsObject().Select(kv => kv.Key));
        Assert.Null(pathItem["get"]!.AsObject()["description"]?.GetValue<string>());

        var postSecurity = pathItem["post"]!.AsObject()["security"]!.AsArray();
        Assert.Single(postSecurity);
        Assert.Contains("Bearer", postSecurity[0]!.AsObject().Select(kv => kv.Key));
        var postDesc = pathItem["post"]!.AsObject()["description"]!.GetValue<string>();
        Assert.Contains("Administrator", postDesc, StringComparison.Ordinal);
    }
}
