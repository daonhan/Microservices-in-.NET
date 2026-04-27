using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ApiGateway.Gateway;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Observability;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Tests.Integration;

internal sealed class GatewayTestHarness : IAsyncDisposable
{
    private static readonly int[] OcelotRouteIndices = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    public string TestIssuer { get; }
    private readonly WebApplication _app;
    private readonly WebApplication _authApp;
    private readonly RSA _rsa;

    public HttpClient Client { get; }

    private GatewayTestHarness(WebApplication app, WebApplication authApp, HttpClient client, string issuer, RSA rsa)
    {
        _app = app;
        _authApp = authApp;
        Client = client;
        TestIssuer = issuer;
        _rsa = rsa;
    }

    public static async Task<GatewayTestHarness> CreateAsync(
        string provider,
        string downstreamStubBaseUrl,
        string? environmentName = null)
    {
        var rsa = RSA.Create(2048);
        var authPort = AllocatePort();
        var issuer = $"http://127.0.0.1:{authPort}";

        var authBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        var authApp = authBuilder.Build();

        authApp.MapGet("/.well-known/openid-configuration", () => Results.Json(new { issuer, jwks_uri = $"{issuer}/.well-known/jwks.json" }));
        authApp.MapGet("/.well-known/jwks.json", () =>
        {
            var p = rsa.ExportParameters(false);
            return Results.Json(new { keys = new[] { new { kty = "RSA", use = "sig", kid = "test-kid", alg = "RS256", n = Base64UrlEncode(p.Modulus!), e = Base64UrlEncode(p.Exponent!) } } });
        });

        authApp.Urls.Add(issuer);
        await authApp.StartAsync();

        var builder = environmentName is null
            ? WebApplication.CreateBuilder()
            : WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });

        builder.Configuration["Gateway:Provider"] = provider;
        builder.Configuration["OpenTelemetry:OtlpExporterEndpoint"] = "http://localhost:4317";
        builder.Configuration["Authentication:AuthMicroserviceBaseAddress"] = issuer;

        if (provider == "Yarp")
        {
            foreach (var cluster in new[] { "auth-cluster", "product-cluster", "basket-cluster", "order-cluster", "inventory-cluster", "shipping-cluster", "payment-cluster" })
            {
                builder.Configuration[$"ReverseProxy:Clusters:{cluster}:Destinations:default:Address"] = downstreamStubBaseUrl;
            }
        }

        builder.Logging.ClearProviders();
        builder.AddConfiguredGateway();

        if (provider == "Ocelot")
        {
            var uri = new Uri(downstreamStubBaseUrl);
            var overrides = new Dictionary<string, string?>();
            foreach (var idx in OcelotRouteIndices)
            {
                overrides[$"Routes:{idx}:DownstreamHostAndPorts:0:Host"] = uri.Host;
                overrides[$"Routes:{idx}:DownstreamHostAndPorts:0:Port"] = uri.Port.ToString(CultureInfo.InvariantCulture);
            }
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.AddPlatformObservability("ApiGateway", customTracing: tracing => tracing.AddSource("Yarp.ReverseProxy"));
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
        return new GatewayTestHarness(app, authApp, client, issuer, rsa);
    }

    public string CreateJwt(string? role = null)
    {
        var rsaKey = new RsaSecurityKey(_rsa) { KeyId = "test-kid" };
        var credentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256);

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
        await _authApp.StopAsync();
        await _authApp.DisposeAsync();
        _rsa.Dispose();
    }

    private static int AllocatePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string Base64UrlEncode(byte[] input) => Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
