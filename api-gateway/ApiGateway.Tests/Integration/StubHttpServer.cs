using System.Collections.Concurrent;
using System.Net.Sockets;

namespace ApiGateway.Tests.Integration;

internal sealed class StubHttpServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }
    public ConcurrentBag<(string Method, string Path)> ReceivedRequests { get; } = new();

    public StubHttpServer()
    {
        var port = AllocatePort();
        BaseUrl = $"http://localhost:{port}";

        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.WebHost.UseUrls(BaseUrl);
        appBuilder.Logging.ClearProviders();
        _app = appBuilder.Build();

        _app.Run(async context =>
        {
            ReceivedRequests.Add((context.Request.Method, context.Request.Path.Value ?? string.Empty));
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("{}");
        });
    }

    public Task StartAsync() => _app.StartAsync();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static int AllocatePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
