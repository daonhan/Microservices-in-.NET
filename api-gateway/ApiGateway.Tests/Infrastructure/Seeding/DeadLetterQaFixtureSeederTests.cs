using ApiGateway.Infrastructure.Seeding;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Tests.Infrastructure.Seeding;

public class DeadLetterQaFixtureSeederTests
{
    [Fact]
    public void GivenEmptyDatabase_WhenSeederRunsTwice_ThenExactlyFiveRowsExist()
    {
        using var context = NewContext();

        DeadLetterQaFixtureSeeder.SeedQaDeadLetterFixture(context);
        DeadLetterQaFixtureSeeder.SeedQaDeadLetterFixture(context);

        var rows = context.DeadLetterMessages
            .Where(m => m.Service == DeadLetterQaFixtureSeeder.OperatorService)
            .ToList();
        Assert.Equal(5, rows.Count);
        Assert.Equal(
            new[]
            {
                DeadLetterQaFixtureSeeder.ListId,
                DeadLetterQaFixtureSeeder.ReplayId,
                DeadLetterQaFixtureSeeder.BatchReplayAId,
                DeadLetterQaFixtureSeeder.BatchReplayBId,
                DeadLetterQaFixtureSeeder.DiscardId,
            }.OrderBy(id => id),
            rows.Select(r => r.Id).OrderBy(id => id));
    }

    [Fact]
    public void GivenMutatingFixturesMarkedReplayedOrDiscarded_WhenSeederReruns_ThenAllFourResetToPending()
    {
        using var context = NewContext();
        DeadLetterQaFixtureSeeder.SeedQaDeadLetterFixture(context);

        foreach (var id in new[] { DeadLetterQaFixtureSeeder.ReplayId, DeadLetterQaFixtureSeeder.BatchReplayAId })
        {
            var row = context.DeadLetterMessages.Single(m => m.Id == id);
            row.Status = DeadLetterStatus.Replayed;
            row.ReplayedAt = DateTime.UtcNow;
            row.ReplayedBy = "test";
        }
        foreach (var id in new[] { DeadLetterQaFixtureSeeder.BatchReplayBId, DeadLetterQaFixtureSeeder.DiscardId })
        {
            var row = context.DeadLetterMessages.Single(m => m.Id == id);
            row.Status = DeadLetterStatus.Discarded;
            row.DiscardedAt = DateTime.UtcNow;
            row.DiscardedBy = "test";
            row.DiscardReason = "test";
        }
        context.SaveChanges();

        DeadLetterQaFixtureSeeder.SeedQaDeadLetterFixture(context);

        foreach (var id in new[]
                 {
                     DeadLetterQaFixtureSeeder.ReplayId,
                     DeadLetterQaFixtureSeeder.BatchReplayAId,
                     DeadLetterQaFixtureSeeder.BatchReplayBId,
                     DeadLetterQaFixtureSeeder.DiscardId,
                 })
        {
            var row = context.DeadLetterMessages.AsNoTracking().Single(m => m.Id == id);
            Assert.Equal(DeadLetterStatus.Pending, row.Status);
            Assert.Null(row.ReplayedAt);
            Assert.Null(row.ReplayedBy);
            Assert.Null(row.DiscardedAt);
            Assert.Null(row.DiscardedBy);
            Assert.Null(row.DiscardReason);
        }
    }

    private static DeadLetterDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DeadLetterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DeadLetterDbContext(options);
    }
}
