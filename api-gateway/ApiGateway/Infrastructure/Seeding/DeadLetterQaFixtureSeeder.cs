using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;

namespace ApiGateway.Infrastructure.Seeding;

public static class DeadLetterQaFixtureSeeder
{
    public const string OperatorService = "qa-operator";
    public const string OperatorEventType = "Qa.OperatorSmokeEvent";
    public const string InertReplaySinkQueue = "qa-dlq-replay-sink";

    public static readonly Guid ListId = new("f0000000-0000-0000-0000-000000000001");
    public static readonly Guid ReplayId = new("f0000000-0000-0000-0000-000000000002");
    public static readonly Guid BatchReplayAId = new("f0000000-0000-0000-0000-000000000003");
    public static readonly Guid BatchReplayBId = new("f0000000-0000-0000-0000-000000000004");
    public static readonly Guid DiscardId = new("f0000000-0000-0000-0000-000000000005");

    internal static readonly DateTime SeedFailedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    internal static readonly Guid SeedCorrelationId = new("f0000000-0000-0000-0000-0000000000a1");

    internal static readonly Guid[] FixtureIds =
    [
        ListId, ReplayId, BatchReplayAId, BatchReplayBId, DiscardId,
    ];

    internal static readonly Guid[] MutatingFixtureIds =
    [
        ReplayId, BatchReplayAId, BatchReplayBId, DiscardId,
    ];

    public static void SeedQaDeadLetterFixture(this WebApplication webApp)
    {
        using var scope = webApp.Services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DeadLetterDbContext>();
        SeedQaDeadLetterFixture(context);
    }

    internal static void SeedQaDeadLetterFixture(DeadLetterDbContext context)
    {
        var existingIds = context.DeadLetterMessages
            .Where(m => FixtureIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToHashSet();

        foreach (var id in FixtureIds)
        {
            if (existingIds.Contains(id))
            {
                continue;
            }
            context.DeadLetterMessages.Add(new DeadLetterMessage
            {
                Id = id,
                EventType = OperatorEventType,
                RoutingKey = InertReplaySinkQueue,
                OriginalQueue = InertReplaySinkQueue,
                Service = OperatorService,
                Payload = "{}",
                FailureReason = "qa seed",
                Attempts = 0,
                FailedAt = SeedFailedAt,
                Status = DeadLetterStatus.Pending,
                CorrelationId = SeedCorrelationId,
                Origin = DeadLetterOrigin.DeadLetter,
            });
        }

        // Reset the four mutating targets back to Pending on every boot so reruns do not 409.
        var mutating = context.DeadLetterMessages
            .Where(m => MutatingFixtureIds.Contains(m.Id))
            .ToList();
        foreach (var row in mutating)
        {
            row.Status = DeadLetterStatus.Pending;
            row.ReplayedAt = null;
            row.ReplayedBy = null;
            row.DiscardedAt = null;
            row.DiscardedBy = null;
            row.DiscardReason = null;
        }

        context.SaveChanges();
    }
}
