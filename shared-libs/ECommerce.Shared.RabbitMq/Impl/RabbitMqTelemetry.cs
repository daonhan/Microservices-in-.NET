using System.Diagnostics;
using ECommerce.Shared.Kernel.TelemetryConventions;

namespace ECommerce.Shared.Infrastructure.RabbitMq;

public class RabbitMqTelemetry
{
    public const string ActivitySourceName = RabbitMqTelemetryNames.ActivitySourceName;
    public ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}
