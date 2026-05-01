namespace ECommerce.Shared.Infrastructure.AzureServiceBus;

public class AzureServiceBusOptions
{
    public const string AzureServiceBusSectionName = "AzureServiceBus";

    /// <summary>
    /// Connection string to the Azure Service Bus namespace.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Topic name used as the fanout exchange equivalent. All events are published here.
    /// </summary>
    public string TopicName { get; set; } = "ecommerce-topic";
}
