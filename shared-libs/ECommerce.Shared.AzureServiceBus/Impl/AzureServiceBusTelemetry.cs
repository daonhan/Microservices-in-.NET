using System.Diagnostics;
using ECommerce.Shared.Kernel.TelemetryConventions;

namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public class AzureServiceBusTelemetry
{
    public const string ActivitySourceName = AzureServiceBusTelemetryNames.ActivitySourceName;
    public ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}
