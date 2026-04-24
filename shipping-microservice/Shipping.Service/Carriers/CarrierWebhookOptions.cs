namespace Shipping.Service.Carriers;

public class CarrierWebhookOptions
{
    public const string SectionName = "CarrierWebhooks";

    /// <summary>
    /// Per-carrier shared secrets, keyed by carrier key. Requests to the webhook
    /// endpoint must present the matching secret in the
    /// <c>X-Carrier-Secret</c> header.
    /// </summary>
    public Dictionary<string, string> SharedSecrets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Poll interval for <c>CarrierPollingService</c>, in seconds.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 30;
}
