using ApiGateway.Features.Operator.GetFailureDetail;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ApiGateway.Tests.Operator;

public sealed class DeadLetterDetailEndpointTests
{
    private static DeadLetterDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DeadLetterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DeadLetterDbContext(options);
    }

    [Fact]
    public async Task Given_unknown_id_When_GetFailureDetail_Then_returns_NotFound()
    {
        await using var ctx = NewContext();
        var configuration = new ConfigurationBuilder().Build();
        var handler = new GetFailureDetailHandler(ctx, configuration);

        var result = await handler.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task Given_message_with_correlation_id_and_configured_base_url_When_GetFailureDetail_Then_returns_traceUrl()
    {
        var correlationId = Guid.NewGuid();
        var msg = NewMessage(correlationId);
        await using var ctx = NewContext();
        ctx.DeadLetterMessages.Add(msg);
        await ctx.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Operator:TraceUiBaseUrl"] = "http://jaeger:16686/trace/",
            })
            .Build();
        var handler = new GetFailureDetailHandler(ctx, configuration);

        var result = await handler.HandleAsync(msg.Id, CancellationToken.None);

        var ok = Assert.IsType<Ok<DeadLetterDetailResponse>>(result);
        Assert.Equal($"http://jaeger:16686/trace/{correlationId}", ok.Value!.TraceUrl);
        Assert.Equal(correlationId, ok.Value.Message.CorrelationId);
    }

    [Fact]
    public async Task Given_message_with_correlation_id_but_no_base_url_When_GetFailureDetail_Then_traceUrl_is_null()
    {
        var msg = NewMessage(Guid.NewGuid());
        await using var ctx = NewContext();
        ctx.DeadLetterMessages.Add(msg);
        await ctx.SaveChangesAsync();

        var configuration = new ConfigurationBuilder().Build();
        var handler = new GetFailureDetailHandler(ctx, configuration);

        var result = await handler.HandleAsync(msg.Id, CancellationToken.None);

        var ok = Assert.IsType<Ok<DeadLetterDetailResponse>>(result);
        Assert.Null(ok.Value!.TraceUrl);
    }

    [Fact]
    public async Task Given_message_without_correlation_id_When_GetFailureDetail_Then_traceUrl_is_null()
    {
        var msg = NewMessage(correlationId: null);
        await using var ctx = NewContext();
        ctx.DeadLetterMessages.Add(msg);
        await ctx.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Operator:TraceUiBaseUrl"] = "http://jaeger:16686/trace/",
            })
            .Build();
        var handler = new GetFailureDetailHandler(ctx, configuration);

        var result = await handler.HandleAsync(msg.Id, CancellationToken.None);

        var ok = Assert.IsType<Ok<DeadLetterDetailResponse>>(result);
        Assert.Null(ok.Value!.TraceUrl);
    }

    private static DeadLetterMessage NewMessage(Guid? correlationId) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "OrderCreatedEvent",
        RoutingKey = "OrderCreatedEvent",
        OriginalQueue = "basket",
        Service = "basket",
        Payload = "{\"orderId\":1}",
        FailureReason = "boom",
        Attempts = 2,
        FailedAt = DateTime.UtcNow,
        Status = DeadLetterStatus.Pending,
        Origin = DeadLetterOrigin.DeadLetter,
        CorrelationId = correlationId,
    };
}
