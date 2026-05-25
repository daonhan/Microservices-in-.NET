namespace ECommerce.Shared.Infrastructure.RabbitMq;

public static class RabbitMqTopology
{
    public const string ExchangeName = "ecommerce-exchange";
    public const string DeadLetterExchangeName = "ecommerce-dlx";
    public const string DeadLetterQueueName = "ecommerce-dlq";

    public const string OriginalQueueHeader = "x-original-queue";
    public const string EventTypeHeader = "x-event-type";
    public const string ServiceHeader = "x-service";
    public const string FailureReasonHeader = "x-failure-reason";
    public const string StackTraceHeader = "x-stack-trace";
    public const string AttemptsHeader = "x-attempts";
    public const string FailedAtHeader = "x-failed-at";
    public const string ReplayedFromHeader = "x-replayed-from";
    public const string CorrelationIdHeader = "x-correlation-id";
}
