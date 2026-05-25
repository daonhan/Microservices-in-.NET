using System.Collections.Concurrent;

namespace ApiGateway.Tests.Integration;

internal sealed class StubHttpServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; private set; } = null!;
    public ConcurrentBag<(string Method, string Path)> ReceivedRequests { get; } = new();

    public StubHttpServer()
    {
        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.WebHost.UseUrls(TestServerAddresses.DynamicLoopbackUrl);
        appBuilder.Logging.ClearProviders();
        _app = appBuilder.Build();

        _app.Run(async context =>
        {
            ReceivedRequests.Add((context.Request.Method, context.Request.Path.Value ?? string.Empty));
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("{}");
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
