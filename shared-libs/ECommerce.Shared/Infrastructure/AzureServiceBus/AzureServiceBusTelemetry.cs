using System.Diagnostics;

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public class AzureServiceBusTelemetry
{
    public const string ActivitySourceName = "AzureServiceBusEventBus";
    public ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}
