namespace ECommerce.Shared.Infrastructure.Messaging;

public class MessagingOptions
{
    public const string MessagingSectionName = "Messaging";

    public const string RabbitMqProvider = "RabbitMq";
    public const string AzureServiceBusProvider = "AzureServiceBus";

    public string Provider { get; set; } = RabbitMqProvider;
}
