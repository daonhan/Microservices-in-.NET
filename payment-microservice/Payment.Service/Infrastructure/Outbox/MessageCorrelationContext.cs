namespace Payment.Service.Infrastructure.Outbox;

/// <summary>
/// Scoped carrier for correlation metadata flowing from an inbound saga command
/// onto the outbox integration event published by <see cref="DomainEventOutboxInterceptor"/>.
/// Saga command handlers set the three properties before invoking the write path so the
/// interceptor can stamp the outbound event without each handler hand-crafting its reply.
/// HTTP write paths leave the properties at default (null), matching pre-refactor behavior
/// where the discarded <c>PaymentContext.Translate</c> switch attached no correlation metadata.
/// </summary>
internal sealed class MessageCorrelationContext
{
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public Guid? SagaId { get; set; }
}
