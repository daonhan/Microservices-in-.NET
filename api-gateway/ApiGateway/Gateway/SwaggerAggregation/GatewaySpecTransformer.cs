using System.Text.Json.Nodes;

namespace ApiGateway.Gateway.SwaggerAggregation;

public static class GatewaySpecTransformer
{
    public static string Transform(string rawJson, string serviceTag)
    {
        var root = JsonNode.Parse(rawJson) as JsonObject
            ?? throw new InvalidOperationException("Downstream OpenAPI document is not a JSON object.");

        root["servers"] = new JsonArray(new JsonObject { ["url"] = "/" });

        if (root["paths"] is JsonObject paths)
        {
            foreach (var pathEntry in paths)
            {
                if (pathEntry.Value is not JsonObject operations)
                {
                    continue;
                }

                foreach (var opEntry in operations)
                {
                    if (opEntry.Value is not JsonObject operation)
                    {
                        continue;
                    }

                    operation["tags"] = new JsonArray(serviceTag);
                }
            }
        }

        return root.ToJsonString();
    }
}
