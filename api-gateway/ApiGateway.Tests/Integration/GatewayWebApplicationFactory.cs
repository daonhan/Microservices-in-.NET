using System.Globalization;
using System.Net.Sockets;
using ApiGateway.Gateway;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Observability;

namespace ApiGateway.Tests.Integration;

internal sealed class GatewayTestHarness : IAsyncDisposable
{
    private readonly WebApplication _app;

    public HttpClient Client { get; }

    private GatewayTestHarness(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public static async Task<GatewayTestHarness> CreateAsync(string provider, string authStubBaseUrl)
    {
        var builder = WebApplication.CreateBuilder();

        // Set provider and downstream addresses BEFORE service registration reads them
        builder.Configuration["Gateway:Provider"] = provider;
        builder.Configuration["OpenTelemetry:OtlpExporterEndpoint"] = "http://localhost:4317";
        builder.Configuration["Authentication:AuthMicroserviceBaseAddress"] = "http://localhost:8003";

        if (provider == "Yarp")
        {
            builder.Configuration["ReverseProxy:Clusters:auth-cluster:Destinations:default:Address"] = authStubBaseUrl;
        }

        builder.Logging.ClearProviders();

        // Service registration reads Gateway:Provider (already set above)
        builder.AddConfiguredGateway();

        // For Ocelot: inject stub destination AFTER ocelot.json was loaded (last-added source wins)
        if (provider == "Ocelot")
        {
            var uri = new Uri(authStubBaseUrl);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Routes:0:DownstreamHostAndPorts:0:Host"] = uri.Host,
                ["Routes:0:DownstreamHostAndPorts:0:Port"] = uri.Port.ToString(CultureInfo.InvariantCulture)
            });
        }

        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.AddPlatformObservability("ApiGateway");
        builder.Services.AddPlatformHealthChecks();

        var gatewayPort = AllocatePort();
        builder.WebHost.UseUrls($"http://localhost:{gatewayPort}");

        var app = builder.Build();
        app.UsePrometheusExporter();
        app.MapPlatformHealthChecks();
        app.UseJwtAuthentication();
        await app.UseConfiguredGatewayAsync();
        await app.StartAsync();

        var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{gatewayPort}") };
        return new GatewayTestHarness(app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
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
