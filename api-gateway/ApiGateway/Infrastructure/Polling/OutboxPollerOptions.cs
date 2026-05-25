namespace ApiGateway.Infrastructure.Polling;

public sealed class OutboxPollerOptions
{
    public const string SectionName = "OutboxPolling";

    public bool Enabled { get; set; }

    public int IntervalSeconds { get; set; } = 60;

    public string AuthTokenEndpoint { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public List<OutboxPollerServiceOptions> Services { get; set; } = new();
}

public sealed class OutboxPollerServiceOptions
{
    public string Name { get; set; } = string.Empty;

    public string FailedOutboxEndpoint { get; set; } = string.Empty;
}
