namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public sealed record AzureServiceBusTopologyProvisioningDecision(
    bool ShouldProvision,
    bool IsEmulator,
    string Policy,
    string Reason);

public static class AzureServiceBusTopologyProvisioningPolicy
{
    public static AzureServiceBusTopologyProvisioningDecision Evaluate(AzureServiceBusOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var policy = NormalizePolicy(options.AutoProvisionTopology);
        var isEmulator = IsEmulatorConnectionString(options.ConnectionString);

        return policy switch
        {
            AzureServiceBusOptions.AutoProvisionTopologyAlways => new(
                true,
                isEmulator,
                policy,
                "AutoProvisionTopology=Always requests topology provisioning."),
            AzureServiceBusOptions.AutoProvisionTopologyNever => new(
                false,
                isEmulator,
                policy,
                "AutoProvisionTopology=Never disables topology provisioning."),
            AzureServiceBusOptions.AutoProvisionTopologyAuto when isEmulator => new(
                true,
                isEmulator,
                policy,
                "AutoProvisionTopology=Auto enables topology provisioning for emulator connection strings."),
            AzureServiceBusOptions.AutoProvisionTopologyAuto => new(
                false,
                isEmulator,
                policy,
                "AutoProvisionTopology=Auto skips topology provisioning for non-emulator connection strings."),
            _ => throw new InvalidOperationException(
                $"Unknown Azure Service Bus topology provisioning policy '{options.AutoProvisionTopology}'."),
        };
    }

    public static bool IsValidPolicy(string? policy) =>
        string.Equals(policy, AzureServiceBusOptions.AutoProvisionTopologyAuto, StringComparison.OrdinalIgnoreCase)
        || string.Equals(policy, AzureServiceBusOptions.AutoProvisionTopologyAlways, StringComparison.OrdinalIgnoreCase)
        || string.Equals(policy, AzureServiceBusOptions.AutoProvisionTopologyNever, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePolicy(string? policy)
    {
        if (string.Equals(policy, AzureServiceBusOptions.AutoProvisionTopologyAuto, StringComparison.OrdinalIgnoreCase))
        {
            return AzureServiceBusOptions.AutoProvisionTopologyAuto;
        }

        if (string.Equals(policy, AzureServiceBusOptions.AutoProvisionTopologyAlways, StringComparison.OrdinalIgnoreCase))
        {
            return AzureServiceBusOptions.AutoProvisionTopologyAlways;
        }

        if (string.Equals(policy, AzureServiceBusOptions.AutoProvisionTopologyNever, StringComparison.OrdinalIgnoreCase))
        {
            return AzureServiceBusOptions.AutoProvisionTopologyNever;
        }

        throw new InvalidOperationException(
            $"Unknown Azure Service Bus topology provisioning policy '{policy}'. Valid values: Auto, Always, Never.");
    }

    private static bool IsEmulatorConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim();

            if (string.Equals(key, "UseDevelopmentEmulator", StringComparison.OrdinalIgnoreCase)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
