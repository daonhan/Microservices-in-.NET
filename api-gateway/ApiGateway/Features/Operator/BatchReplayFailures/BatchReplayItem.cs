namespace ApiGateway.Features.Operator.BatchReplayFailures;

public sealed record BatchReplayItem(Guid Id, string Status, Guid? NewMessageId, string? Reason);
