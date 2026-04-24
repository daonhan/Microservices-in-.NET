using System.Text.Json;

namespace Shipping.Service.Carriers;

/// <summary>
/// Shared webhook payload parser for fake carriers. Expects
/// <c>{ "trackingNumber": "...", "statusCode": &lt;int&gt;, "detail": "..." }</c>
/// where <c>statusCode</c> maps to <see cref="CarrierStatusCode"/>.
/// </summary>
internal static class FakeCarrierWebhookParser
{
    public static bool TryParse(JsonElement payload, out CarrierWebhookUpdate? update)
    {
        update = null;

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!payload.TryGetProperty("trackingNumber", out var trackingElement)
            || trackingElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var tracking = trackingElement.GetString();
        if (string.IsNullOrWhiteSpace(tracking))
        {
            return false;
        }

        if (!payload.TryGetProperty("statusCode", out var statusElement)
            || statusElement.ValueKind != JsonValueKind.Number
            || !statusElement.TryGetInt32(out var statusInt)
            || !Enum.IsDefined(typeof(CarrierStatusCode), statusInt))
        {
            return false;
        }

        string? detail = null;
        if (payload.TryGetProperty("detail", out var detailElement)
            && detailElement.ValueKind == JsonValueKind.String)
        {
            detail = detailElement.GetString();
        }

        update = new CarrierWebhookUpdate(
            TrackingNumber: tracking,
            Status: new CarrierStatus((CarrierStatusCode)statusInt, detail));
        return true;
    }
}
