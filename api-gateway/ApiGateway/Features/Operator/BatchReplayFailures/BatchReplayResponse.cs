namespace ApiGateway.Features.Operator.BatchReplayFailures;

public sealed record BatchReplayResponse(IReadOnlyList<BatchReplayItem> Items);
