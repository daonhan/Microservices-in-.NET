namespace ApiGateway.Infrastructure.Polling;

public sealed record OutboxFailureItem(
    Guid Id,
    string EventType,
    string Data,
    int Attempts,
    string? LastError,
    DateTime? LastAttemptAt,
    Guid? CorrelationId);
