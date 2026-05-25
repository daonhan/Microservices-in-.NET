namespace ApiGateway.Features.Operator.BatchReplayFailures;

public sealed record BatchReplayRequest(IReadOnlyList<Guid> Ids);
