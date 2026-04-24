using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Gateway.SwaggerAggregation;

public static class GatewayRouteDiscovery
{
    private const string PathPatternTransformKey = "PathPattern";

    public static IReadOnlyList<GatewayRouteInfo> DiscoverRoutes(IProxyConfigProvider provider)
    {
        var config = provider.GetConfig();
        var list = new List<GatewayRouteInfo>();

        foreach (var route in config.Routes)
        {
            if (string.IsNullOrEmpty(route.ClusterId) || string.IsNullOrEmpty(route.Match.Path))
            {
                continue;
            }

            var internalPath = ExtractInternalPath(route);
            var methods = route.Match.Methods is null
                ? Array.Empty<string>()
                : route.Match.Methods.ToArray();

            list.Add(new GatewayRouteInfo(
                RouteId: route.RouteId,
                GatewayPathPattern: route.Match.Path,
                Methods: methods,
                InternalPathPattern: internalPath,
                AuthorizationPolicy: route.AuthorizationPolicy ?? "Default",
                ClusterId: route.ClusterId));
        }

        return list;
    }

    private static string ExtractInternalPath(RouteConfig route)
    {
        if (route.Transforms is null)
        {
            return route.Match.Path!;
        }

        foreach (var transform in route.Transforms)
        {
            if (transform.TryGetValue(PathPatternTransformKey, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return route.Match.Path!;
    }
}
