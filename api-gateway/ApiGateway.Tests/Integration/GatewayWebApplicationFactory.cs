using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using ApiGateway.Gateway;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Observability;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Tests.Integration;

internal sealed class GatewayTestHarness : IAsyncDisposable
{
    // Indices match ocelot.json route order.
    // 0=login(auth) 1=product-get 2=product-write 3=basket 4=order
    // 5=inventory-list 6=inventory-read 7=inventory-backorder 8=inventory-write
    private static readonly int[] OcelotRouteIndices = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    internal const string TestIssuer = "http://localhost:8003";

    private readonly WebApplication _app;

    public HttpClient Client { get; }

    private GatewayTestHarness(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public static Task<GatewayTestHarness> CreateAsync(string provider, string downstreamStubBaseUrl)
    {
        var builder = WebApplication.CreateBuilder();

        // Set provider and downstream addresses BEFORE service registration reads them
        builder.Configuration["Gateway:Provider"] = provider;
        builder.Configuration["OpenTelemetry:OtlpExporterEndpoint"] = "http://localhost:4317";
        builder.Configuration["Authentication:AuthMicroserviceBaseAddress"] = TestIssuer;

        if (provider == "Yarp")
        {
            // Route every cluster destination at the single stub so tests can
            // assert method + transformed path without spinning up per-service stubs.
            foreach (var cluster in new[]
            {
                "auth-cluster", "product-cluster", "basket-cluster",
                "order-cluster", "inventory-cluster"
            })
            {
                builder.Configuration[$"ReverseProxy:Clusters:{cluster}:Destinations:default:Address"]
                    = downstreamStubBaseUrl;
            }
        }

        builder.Logging.ClearProviders();

        builder.AddConfiguredGateway();

        if (provider == "Ocelot")
        {
            // Ocelot reads its routes from ocelot.json; override every route's
            // downstream host/port to point at the stub (last config source wins).
            var uri = new Uri(downstreamStubBaseUrl);
            var overrides = new Dictionary<string, string?>();
            foreach (var idx in OcelotRouteIndices)
            {
                overrides[$"Routes:{idx}:DownstreamHostAndPorts:0:Host"] = uri.Host;
                overrides[$"Routes:{idx}:DownstreamHostAndPorts:0:Port"]
                    = uri.Port.ToString(CultureInfo.InvariantCulture);
            }
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.AddPlatformObservability(
            "ApiGateway",
            customTracing: tracing => tracing.AddSource("Yarp.ReverseProxy"));
        builder.Services.AddPlatformHealthChecks();

        var gatewayPort = AllocatePort();
        builder.WebHost.UseUrls($"http://localhost:{gatewayPort}");

        return BuildAndStartAsync(builder, gatewayPort);
    }

    private static async Task<GatewayTestHarness> BuildAndStartAsync(WebApplicationBuilder builder, int gatewayPort)
    {
        var app = builder.Build();
        app.UsePrometheusExporter();
        app.MapPlatformHealthChecks();
        app.UseJwtAuthentication();
        await app.UseConfiguredGatewayAsync();
        await app.StartAsync();

        var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{gatewayPort}") };
        return new GatewayTestHarness(app, client);
    }

    public static string CreateJwt(string? role = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationExtensions.SecurityKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Name, "test-user") };
        if (role is not null)
        {
            claims.Add(new Claim("user_role", role));
        }

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
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
