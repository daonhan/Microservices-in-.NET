using System.Text.Json;
using ApiGateway.Infrastructure.Polling;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiGateway.Tests.Infrastructure.Polling;

public sealed class OutboxFailurePollerTests : IAsyncLifetime, IDisposable
{
    private StubAuthServer _auth = null!;
    private StubOutboxService _orderStub = null!;
    private StubOutboxService _productStub = null!;

    public async Task InitializeAsync()
    {
        _auth = new StubAuthServer();
        _orderStub = new StubOutboxService();
        _productStub = new StubOutboxService();
        await Task.WhenAll(_auth.StartAsync(), _orderStub.StartAsync(), _productStub.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await _auth.DisposeAsync();
        await _orderStub.DisposeAsync();
        await _productStub.DisposeAsync();
    }

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact]
    public async Task Given_per_service_failed_rows_When_PollOnceAsync_Then_upserts_with_origin_Outbox()
    {
        var orderFailureId = Guid.NewGuid();
        var productFailureId = Guid.NewGuid();
        _orderStub.Failures = new[]
        {
            new OutboxFailureItem(orderFailureId, "OrderCreatedEvent", "{\"orderId\":1}", 3, "boom", DateTime.UtcNow, Guid.NewGuid()),
        };
        _productStub.Failures = new[]
        {
            new OutboxFailureItem(productFailureId, "ProductCreatedEvent", "{\"sku\":\"x\"}", 2, "publish-fail", DateTime.UtcNow, null),
        };

        var (poller, scopeFactory) = BuildPoller("test-token");

        await poller.PollOnceAsync(CancellationToken.None);

        using var scope = scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DeadLetterDbContext>();
        var rows = await ctx.DeadLetterMessages.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(DeadLetterOrigin.Outbox, r.Origin));
        Assert.Contains(rows, r => r.Id == orderFailureId && r.Service == "order" && r.EventType == "OrderCreatedEvent");
        Assert.Contains(rows, r => r.Id == productFailureId && r.Service == "product" && r.FailureReason == "publish-fail");
    }

    [Fact]
    public async Task Given_existing_outbox_row_When_PollOnceAsync_Then_updates_attempts_and_does_not_duplicate()
    {
        var failureId = Guid.NewGuid();
        _orderStub.Failures = new[]
        {
            new OutboxFailureItem(failureId, "OrderCreatedEvent", "{}", 1, "transient", DateTime.UtcNow, null),
        };

        var (poller, scopeFactory) = BuildPoller("test-token");

        await poller.PollOnceAsync(CancellationToken.None);

        _orderStub.Failures = new[]
        {
            new OutboxFailureItem(failureId, "OrderCreatedEvent", "{}", 5, "still-failing", DateTime.UtcNow, null),
        };

        await poller.PollOnceAsync(CancellationToken.None);

        using var scope = scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DeadLetterDbContext>();
        var rows = await ctx.DeadLetterMessages.AsNoTracking().ToListAsync();
        var single = Assert.Single(rows);
        Assert.Equal(5, single.Attempts);
        Assert.Equal("still-failing", single.FailureReason);
    }

    [Fact]
    public async Task Given_seeded_rows_When_filter_origin_Outbox_Then_only_outbox_rows_returned()
    {
        var (_, scopeFactory) = BuildPoller("test-token");

        using (var seedScope = scopeFactory.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<DeadLetterDbContext>();
            ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.DeadLetter));
            ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.Outbox));
            ctx.DeadLetterMessages.Add(NewMessage(DeadLetterOrigin.Outbox));
            await ctx.SaveChangesAsync();
        }

        using var queryScope = scopeFactory.CreateScope();
        var queryCtx = queryScope.ServiceProvider.GetRequiredService<DeadLetterDbContext>();

        var page = await queryCtx.ListAsync(new DeadLetterFilter(Origin: DeadLetterOrigin.Outbox));

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, m => Assert.Equal(DeadLetterOrigin.Outbox, m.Origin));
    }

    private static DeadLetterMessage NewMessage(DeadLetterOrigin origin) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "Evt",
        RoutingKey = "Evt",
        OriginalQueue = "svc",
        Service = "svc",
        Payload = "{}",
        FailureReason = "x",
        Attempts = 0,
        FailedAt = DateTime.UtcNow,
        Status = DeadLetterStatus.Pending,
        Origin = origin,
    };

    private (OutboxFailurePoller Poller, IServiceScopeFactory ScopeFactory) BuildPoller(string expectedToken)
    {
        _auth.IssuedToken = expectedToken;

        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<DeadLetterDbContext>(options => options.UseInMemoryDatabase(dbName));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var options = new OutboxPollerOptions
        {
            Enabled = true,
            IntervalSeconds = 1,
            AuthTokenEndpoint = $"{_auth.BaseUrl}/token",
            ClientId = "gateway",
            ClientSecret = "secret",
            Services =
            {
                new OutboxPollerServiceOptions { Name = "order", FailedOutboxEndpoint = $"{_orderStub.BaseUrl}/internal/outbox/failed" },
                new OutboxPollerServiceOptions { Name = "product", FailedOutboxEndpoint = $"{_productStub.BaseUrl}/internal/outbox/failed" },
            },
        };

        var client = new OutboxFailureClient(new HttpClient(), options);
        var poller = new OutboxFailurePoller(scopeFactory, client, options, NullLogger<OutboxFailurePoller>.Instance);
        return (poller, scopeFactory);
    }

    internal sealed class StubAuthServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public string BaseUrl { get; private set; } = null!;
        public string IssuedToken { get; set; } = "test-token";
        public string? LastClientId { get; private set; }
        public string? LastClientSecret { get; private set; }
        public string? LastGrantType { get; private set; }

        public StubAuthServer()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(TestServerAddresses.DynamicLoopbackUrl);
            builder.Logging.ClearProviders();
            _app = builder.Build();

            _app.MapPost("/token", async context =>
            {
                var form = await context.Request.ReadFormAsync();
                LastGrantType = form["grant_type"];
                LastClientId = form["client_id"];
                LastClientSecret = form["client_secret"];
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { token = IssuedToken, expiresIn = 900 }));
            });
        }

        public async Task StartAsync()
        {
            await _app.StartAsync();
            BaseUrl = TestServerAddresses.GetBoundAddress(_app);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    internal sealed class StubOutboxService : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public string BaseUrl { get; private set; } = null!;
        public IReadOnlyList<OutboxFailureItem> Failures { get; set; } = Array.Empty<OutboxFailureItem>();
        public string? LastAuthorizationHeader { get; private set; }

        public StubOutboxService()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(TestServerAddresses.DynamicLoopbackUrl);
            builder.Logging.ClearProviders();
            _app = builder.Build();

            _app.MapGet("/internal/outbox/failed", context =>
            {
                LastAuthorizationHeader = context.Request.Headers.Authorization;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync(JsonSerializer.Serialize(Failures));
            });
        }

        public async Task StartAsync()
        {
            await _app.StartAsync();
            BaseUrl = TestServerAddresses.GetBoundAddress(_app);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
