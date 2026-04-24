using System.Text.Json.Nodes;

namespace ApiGateway.Gateway.SwaggerAggregation;

public static class GatewaySpecTransformer
{
    internal const string AdminClaimNote =
        "Requires the `user_role` claim to equal `Administrator`.";

    private const string BearerSchemeName = "Bearer";

    public static string Transform(
        string rawJson,
        string serviceTag,
        IReadOnlyList<GatewayRouteInfo> clusterRoutes)
    {
        var root = JsonNode.Parse(rawJson) as JsonObject
            ?? throw new InvalidOperationException("Downstream OpenAPI document is not a JSON object.");

        root["servers"] = new JsonArray(new JsonObject { ["url"] = "/" });
        root.Remove("security");

        if (root["paths"] is not JsonObject paths)
        {
            return root.ToJsonString();
        }

        var newPaths = new JsonObject();

        foreach (var pathEntry in paths)
        {
            var internalPath = pathEntry.Key;
            if (pathEntry.Value is not JsonObject operations)
            {
                continue;
            }

            foreach (var opEntry in operations)
            {
                var method = opEntry.Key;
                if (opEntry.Value is not JsonObject operation || !IsHttpMethodKey(method))
                {
                    continue;
                }

                var match = FindBestMatch(internalPath, method, clusterRoutes);
                if (match is null)
                {
                    continue;
                }

                var cloned = (JsonObject)operation.DeepClone();
                cloned["tags"] = new JsonArray(serviceTag);
                ApplySecurity(cloned, match.Value.Route.AuthorizationPolicy);

                if (newPaths[match.Value.GatewayFacingPath] is not JsonObject pathItem)
                {
                    pathItem = new JsonObject();
                    newPaths[match.Value.GatewayFacingPath] = pathItem;
                }
                pathItem[method] = cloned;
            }
        }

        root["paths"] = newPaths;
        return root.ToJsonString();
    }

    private static bool IsHttpMethodKey(string key) =>
        key is "get" or "post" or "put" or "delete" or "patch" or "head" or "options" or "trace";

    private static (GatewayRouteInfo Route, string GatewayFacingPath)? FindBestMatch(
        string internalPath,
        string method,
        IReadOnlyList<GatewayRouteInfo> routes)
    {
        (GatewayRouteInfo Route, string Path, int Specificity)? best = null;

        foreach (var route in routes)
        {
            if (!MethodMatches(route.Methods, method))
            {
                continue;
            }

            if (!TryMatchAndRewrite(internalPath, route, out var facing, out var specificity))
            {
                continue;
            }

            if (best is null || specificity > best.Value.Specificity)
            {
                best = (route, facing, specificity);
            }
        }

        return best is null ? null : (best.Value.Route, best.Value.Path);
    }

    private static bool MethodMatches(IReadOnlyList<string> methods, string opMethod)
    {
        if (methods.Count == 0)
        {
            return true;
        }

        foreach (var m in methods)
        {
            if (string.Equals(m, opMethod, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryMatchAndRewrite(
        string internalPath,
        GatewayRouteInfo route,
        out string gatewayFacingPath,
        out int specificity)
    {
        gatewayFacingPath = string.Empty;
        specificity = 0;

        var openApiSegs = SplitSegments(internalPath);
        var gatewaySegs = SplitSegments(route.InternalPathPattern);

        var catchallIdx = FindCatchallIndex(gatewaySegs);

        if (catchallIdx < 0)
        {
            if (openApiSegs.Count != gatewaySegs.Count)
            {
                return false;
            }

            for (var i = 0; i < gatewaySegs.Count; i++)
            {
                if (!SegmentsMatch(gatewaySegs[i], openApiSegs[i]))
                {
                    return false;
                }
                specificity += IsLiteral(gatewaySegs[i]) ? 10 : 1;
            }

            gatewayFacingPath = route.GatewayPathPattern;
            return true;
        }

        if (openApiSegs.Count < catchallIdx)
        {
            return false;
        }

        for (var i = 0; i < catchallIdx; i++)
        {
            if (!SegmentsMatch(gatewaySegs[i], openApiSegs[i]))
            {
                return false;
            }
            specificity += IsLiteral(gatewaySegs[i]) ? 10 : 1;
        }

        var gatewayPrefix = SplitSegments(route.GatewayPathPattern)
            .TakeWhile(s => !IsCatchall(s))
            .ToList();
        var remaining = openApiSegs.Skip(catchallIdx).ToList();
        var combined = new List<string>(gatewayPrefix);
        combined.AddRange(remaining);

        gatewayFacingPath = combined.Count == 0 ? "/" : "/" + string.Join("/", combined);
        return true;
    }

    private static int FindCatchallIndex(List<string> segments)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            if (IsCatchall(segments[i]))
            {
                return i;
            }
        }
        return -1;
    }

    private static List<string> SplitSegments(string path)
    {
        var trimmed = path.Trim('/');
        if (trimmed.Length == 0)
        {
            return new List<string>();
        }
        return trimmed.Split('/').ToList();
    }

    private static bool IsCatchall(string segment) => segment.StartsWith("{**", StringComparison.Ordinal);

    private static bool IsLiteral(string segment) => !segment.StartsWith('{');

    private static bool SegmentsMatch(string gatewaySeg, string openApiSeg)
    {
        if (IsCatchall(gatewaySeg))
        {
            return true;
        }
        if (gatewaySeg.StartsWith('{'))
        {
            return true;
        }
        return string.Equals(gatewaySeg, openApiSeg, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplySecurity(JsonObject operation, string policy)
    {
        switch (policy)
        {
            case "Anonymous":
                operation["security"] = new JsonArray();
                break;
            case "AdminOnly":
                operation["security"] = BearerRequirement();
                AppendAdminNote(operation);
                break;
            default:
                operation["security"] = BearerRequirement();
                break;
        }
    }

    private static JsonArray BearerRequirement() =>
        new(new JsonObject { [BearerSchemeName] = new JsonArray() });

    private static void AppendAdminNote(JsonObject operation)
    {
        var existing = operation["description"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(existing))
        {
            operation["description"] = AdminClaimNote;
        }
        else if (!existing.Contains(AdminClaimNote, StringComparison.Ordinal))
        {
            operation["description"] = existing + "\n\n" + AdminClaimNote;
        }
    }
}
