namespace ECommerce.Shared.Infrastructure.Outbox;

public class OutboxOptions
{
    public const string OutboxSectionName = "Outbox";
    public int PublishIntervalInSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 5;
}
