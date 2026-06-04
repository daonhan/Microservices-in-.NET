using ECommerce.Shared.Infrastructure.DeadLetter;
using Microsoft.EntityFrameworkCore;

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

    private static readonly DateTime SeedFailedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SeedCorrelationId = new("f0000000-0000-0000-0000-0000000000a1");

    public static void SeedQaDeadLetterFixture(this WebApplication webApp)
    {
        using var scope = webApp.Services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DeadLetterDbContext>();

        foreach (var id in new[] { ListId, ReplayId, BatchReplayAId, BatchReplayBId, DiscardId })
        {
            context.Database.ExecuteSqlInterpolated(
                $@"IF NOT EXISTS (SELECT 1 FROM [dead_letter_messages] WHERE [id] = {id})
                   INSERT INTO [dead_letter_messages]
                       ([id], [event_type], [routing_key], [original_queue], [service], [payload],
                        [failure_reason], [attempts], [failed_at], [status],
                        [correlation_id], [origin])
                   VALUES ({id}, {OperatorEventType}, {InertReplaySinkQueue}, {InertReplaySinkQueue},
                           {OperatorService}, N'{{}}',
                           N'qa seed', 0, {SeedFailedAt}, 0,
                           {SeedCorrelationId}, 0);");
        }

        // Reset the four mutating targets back to Pending on every boot so reruns do not 409.
        foreach (var id in new[] { ReplayId, BatchReplayAId, BatchReplayBId, DiscardId })
        {
            context.Database.ExecuteSqlInterpolated(
                $@"UPDATE [dead_letter_messages]
                   SET [status] = 0,
                       [replayed_at] = NULL, [replayed_by] = NULL,
                       [discarded_at] = NULL, [discarded_by] = NULL, [discard_reason] = NULL
                   WHERE [id] = {id};");
        }
    }
}
