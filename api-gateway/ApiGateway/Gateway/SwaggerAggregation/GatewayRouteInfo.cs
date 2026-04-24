namespace ApiGateway.Gateway.SwaggerAggregation;

public sealed record GatewayRouteInfo(
    string RouteId,
    string GatewayPathPattern,
    IReadOnlyList<string> Methods,
    string InternalPathPattern,
    string AuthorizationPolicy,
    string ClusterId);
