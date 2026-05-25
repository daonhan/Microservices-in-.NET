using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Shared.Tests;

public sealed class DeadLetterDbContextOriginFilterTests
{
    private static DeadLetterDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DeadLetterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DeadLetterDbContext(options);
    }

    private static DeadLetterMessage NewMessage(DeadLetterOrigin origin, DateTime failedAt) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "OrderCreatedEvent",
        RoutingKey = "OrderCreatedEvent",
        OriginalQueue = "basket",
        Service = "basket",
        Payload = "{}",
        FailureReason = "boom",
        Attempts = 1,
        FailedAt = failedAt,
        Status = DeadLetterStatus.Pending,
        Origin = origin
    };

    [Fact]
    public async Task Given_mixed_origins_When_filter_origin_DeadLetter_Then_returns_only_DeadLetter_rows()
    {
        await using var ctx = NewContext();
        ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.DeadLetter, DateTime.UtcNow));
        ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.DeadLetter, DateTime.UtcNow));
        ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.Outbox, DateTime.UtcNow));
        await ctx.SaveChangesAsync();

        var page = await ctx.ListAsync(new DeadLetterFilter(Origin: DeadLetterOrigin.DeadLetter));

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, m => Assert.Equal(DeadLetterOrigin.DeadLetter, m.Origin));
    }

    [Fact]
    public async Task Given_mixed_origins_When_filter_origin_Outbox_Then_returns_only_Outbox_rows()
    {
        await using var ctx = NewContext();
        ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.DeadLetter, DateTime.UtcNow));
        ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.Outbox, DateTime.UtcNow));
        await ctx.SaveChangesAsync();

        var page = await ctx.ListAsync(new DeadLetterFilter(Origin: DeadLetterOrigin.Outbox));

        Assert.Single(page.Items);
        Assert.Equal(DeadLetterOrigin.Outbox, page.Items[0].Origin);
    }

    [Fact]
    public async Task Given_mixed_origins_When_no_origin_filter_Then_returns_all_rows()
    {
        await using var ctx = NewContext();
        ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.DeadLetter, DateTime.UtcNow));
        ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.Outbox, DateTime.UtcNow));
        await ctx.SaveChangesAsync();

        var page = await ctx.ListAsync(new DeadLetterFilter());

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public void Given_new_DeadLetterMessage_When_inspected_Then_default_origin_is_DeadLetter()
    {
        var msg = new DeadLetterMessage();

        Assert.Equal(DeadLetterOrigin.DeadLetter, msg.Origin);
    }
}
