using System.Text;

namespace ApiGateway.Tests.Integration;

internal sealed class SwaggerStubServer : IAsyncDisposable
{
    public const string CannedSpecJson = """
    {
      "openapi": "3.0.1",
      "info": { "title": "Stub", "version": "v1" },
      "servers": [ { "url": "http://stub:8080" } ],
      "paths": {
        "/{id}": {
          "get": { "responses": { "200": { "description": "ok" } } },
          "put": { "responses": { "204": { "description": "nc" } } }
        },
        "/": {
          "post": { "responses": { "201": { "description": "created" } } }
        },
        "/internal/admin-only": {
          "get": { "responses": { "200": { "description": "ok" } } }
        }
      }
    }
    """;

    private readonly WebApplication _app;

    public string BaseUrl { get; private set; } = null!;

    public SwaggerStubServer()
    {
        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.WebHost.UseUrls(TestServerAddresses.DynamicLoopbackUrl);
        appBuilder.Logging.ClearProviders();
        _app = appBuilder.Build();

        _app.Run(async context =>
        {
            if (context.Request.Path.Equals("/swagger/v1/swagger.json", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(CannedSpecJson, Encoding.UTF8);
                return;
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
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
