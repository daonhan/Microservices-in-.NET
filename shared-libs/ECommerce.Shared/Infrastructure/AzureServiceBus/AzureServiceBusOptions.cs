namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public class AzureServiceBusOptions
{
    public const string AzureServiceBusSectionName = "AzureServiceBus";
    public const string AutoProvisionTopologyAuto = "Auto";
    public const string AutoProvisionTopologyAlways = "Always";
    public const string AutoProvisionTopologyNever = "Never";

    /// <summary>
    /// Connection string to the Azure Service Bus namespace.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Optional connection string used only for topology administration.
    /// </summary>
    public string AdministrationConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Topic name used as the fanout exchange equivalent. All events are published here.
    /// </summary>
    public string TopicName { get; set; } = "ecommerce-topic";

    /// <summary>
    /// Controls whether the Azure Service Bus adapter should create topic/subscription topology on startup.
    /// </summary>
    public string AutoProvisionTopology { get; set; } = AutoProvisionTopologyAuto;

    /// <summary>
    /// Subscription name whose dead-letter sub-queue is captured into the dead-letter store.
    /// Empty disables ASB capture; the operator endpoints remain available regardless.
    /// </summary>
    public string DeadLetterCaptureSubscription { get; set; } = string.Empty;
}
