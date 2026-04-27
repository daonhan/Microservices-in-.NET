using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ECommerce.Shared.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ECommerce.Shared.Tests;

/// <summary>
/// Integration tests for the dual-validator in ECommerce.Shared 1.19.0.
/// Spins up a fake "Auth" host that serves /.well-known/jwks.json with the dev
/// RSA public key, then a separate "consumer" host that calls AddJwtAuthentication
/// pointing Authority at the fake Auth host.
/// </summary>
public sealed class DualValidatorTests : IAsyncLifetime, IDisposable
{
    private const string TestKeyId = "auth-dev-2026-04";

    private WebApplication _authApp = null!;     // serves JWKS
    private WebApplication _consumerApp = null!;  // consumer under test
    private HttpClient _client = null!;
    private RSA _rsa = null!;
    private string _issuer = null!;

    public async Task InitializeAsync()
    {
        _rsa = RSA.Create();
        _rsa.ImportFromPem(DevPrivateKeyPem);

        // --- 1. Start a fake Auth host that serves JWKS ---
        var authPort = AllocatePort();
        _issuer = $"http://127.0.0.1:{authPort}";

        var authBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        _authApp = authBuilder.Build();

        // Serve /.well-known/openid-configuration so the framework's metadata pipeline works
        _authApp.MapGet("/.well-known/openid-configuration", () =>
        {
            var config = new
            {
                issuer = _issuer,
                jwks_uri = $"{_issuer}/.well-known/jwks.json"
            };
            return Results.Json(config);
        });

        // Serve /.well-known/jwks.json with the dev public key
        _authApp.MapGet("/.well-known/jwks.json", () =>
        {
            var parameters = _rsa.ExportParameters(false);
            var jwks = new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        kid = TestKeyId,
                        alg = "RS256",
                        n = Base64UrlEncode(parameters.Modulus!),
                        e = Base64UrlEncode(parameters.Exponent!)
                    }
                }
            };
            return Results.Json(jwks);
        });

        _authApp.Urls.Add(_issuer);
        await _authApp.StartAsync();

        // --- 2. Start the consumer host under test ---
        var consumerBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        consumerBuilder.Configuration["Authentication:AuthMicroserviceBaseAddress"] = _issuer;

        consumerBuilder.Services.AddJwtAuthentication(
            consumerBuilder.Configuration,
            consumerBuilder.Environment);

        _consumerApp = consumerBuilder.Build();
        _consumerApp.UseJwtAuthentication();

        _consumerApp.MapGet("/protected", () => Results.Ok("ok"))
            .RequireAuthorization();

        _consumerApp.Urls.Add("http://127.0.0.1:0");
        await _consumerApp.StartAsync();

        var consumerAddress = _consumerApp.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(consumerAddress) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _consumerApp.StopAsync();
        await _consumerApp.DisposeAsync();
        await _authApp.StopAsync();
        await _authApp.DisposeAsync();
        _rsa.Dispose();
    }

    public void Dispose()
    {
        _client?.Dispose();
        _rsa?.Dispose();
    }

    // ---- Tests ----

    [Fact]
    public async Task HS256_token_with_legacy_key_returns_200()
    {
        var token = CreateHs256Token(AuthenticationExtensions.SecurityKey, _issuer);
        var request = NewRequest(token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RS256_token_with_dev_key_returns_200()
    {
        var token = CreateRs256Token(_rsa, TestKeyId, _issuer);
        var request = NewRequest(token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_with_alg_none_returns_401()
    {
        var token = CreateUnsecuredToken(_issuer);
        var request = NewRequest(token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_with_bad_HS256_signature_returns_401()
    {
        var token = CreateHs256Token("WRONG_KEY_XXXXXXXXXXXXXXXXXXXXXXXXX", _issuer);
        var request = NewRequest(token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_HS256_using_public_key_as_secret_returns_401()
    {
        // Algorithm-confusion regression test: must remain green forever
        // Attacker creates an HS256 token using the published RS256 public key (as string/bytes)
        var parameters = _rsa.ExportParameters(false);
        var publicKeyBytes = _rsa.ExportRSAPublicKey();
        var securityKey = new SymmetricSecurityKey(publicKeyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Name, "test-user"),
            new Claim("user_role", "Administrator")
        };

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        
        var request = NewRequest(tokenString);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_with_wrong_RSA_key_returns_401()
    {
        using var wrongRsa = RSA.Create(2048);
        var token = CreateRs256Token(wrongRsa, "wrong-kid", _issuer);
        var request = NewRequest(token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task No_token_returns_401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/protected");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Helpers ----

    private static HttpRequestMessage NewRequest(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string CreateHs256Token(string key, string issuer)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Name, "test-user"),
            new Claim("user_role", "Administrator")
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateRs256Token(RSA rsa, string keyId, string issuer)
    {
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = keyId };
        var credentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Name, "test-user"),
            new Claim("user_role", "Administrator")
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateUnsecuredToken(string issuer)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            $"{{\"name\":\"test-user\",\"user_role\":\"Administrator\",\"iss\":\"{issuer}\",\"exp\":{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}"));

        return $"{header}.{payload}.";
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static int AllocatePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    // Dev private key PEM — same as auth-microservice/Auth.Service/dev-keys/dev-private.pem
    private const string DevPrivateKeyPem = """
        -----BEGIN PRIVATE KEY-----
        MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQCjN/fqeZznn0QP
        wYS9GAQ/qbqlfaDmmtZ9wpyQ0ZMRoeug71UoDtA0e1Ls9Rb1SxFWpbH7WZEyrViF
        g1Q/CJPGlZBqhhE01moz9bDp9EDIIPt/NuI0i7d9qXQK64+3QUmly0+uqWpgU12r
        imCESwNby0U2OKkQI6ptmt+e1SjfrWoGesh0PxWokX1+JLmc403FTzbvQHBlQZOE
        nhtHesD3BzpiBx7fp+8wfJD6Qx4BRGCXF3pLbhueblrbJXnoKF0qkz5/fCShA/7B
        +g9qiFKVa2Ew7V1KMnapjnxspW4nD4omCwS9Tsy/KzolpdunaUkdjyaCV9+Tkqcp
        3OM5lkWjAgMBAAECggEAGP7mwClcrpoU7cbIob7OU8OV+mcZX4eB5qOJn6IAwFgI
        Qk60v1w5ZXgndHN6Txlub9MTRTdrxZOHdXbtzXNuUiCwi6e5ddqKTCfJrqKB00Q9
        z7cjgEGPWba8Nzno+fsNIM9Yhhqa2GKb+zvHWSs1ufaQxGN7/KVBoeRwb54cUti4
        ruluCpvkQ3IwdpXoPv/+YoqwmZL2u8CpLoK40L5IZTZ3J14LhgddrLWdDpTIOmMi
        Tv19Un+J7MfxHuFB5fjYNEdK7Z07A44tbzfseq3qMQ/CO+OUHQzjy/R4wjdj497l
        aSfQXSv9nH0i/5Yw9qM30bYgTjqETcjqgkvI983x1QKBgQDWy1le1GG8/b59+jYe
        EodYxCRrk/pVRDoa0NjZ3pzLxSkaie+rK9xLrwM9nGJ/UquxB1kla1ukhsyhfR2J
        8ULDytz00UepT2K/kcv3DojHqvSzQLYKyjoIkXRAhE6TsbsrK69/5j+CzKihsO/d
        yk6n2CZXYFGBoohBf8VXc95UjwKBgQDCh7ZMLP76/1EoVQc2mWo35T96EnM1A1Q/
        umrjlVk6pnNUlXmrR6yuTlaHBqXenFmJ2KxAft5u3vVz97NPpdEmnX1m87hS2Enm
        EdshAOpwYGVg08SXmEQCymOMU3bmahYjxm62XLGvHvZb4lJsl+/7yL7uxyX4HD5i
        rLl1AVFPrQKBgCkzalopTPIujgqmIxlTnoilXwMPqHYQl0CrjN0FuXfQwtinWsSv
        rhsKYAnCZJZdqjdT3IKz/Ckr/jZ/xFnAYHkkAYwoVKGia6OpeMFUFWKPZU64+/JH
        5ifclrsFZfkon2lhgNF8vfP+A964DNqzQrEpYflirV+7aH1/37+tpxj7AoGBAJA2
        Xe4BGZJn/vmAd5WBjF4dxL4xuVjeu/F2uNE5Ieo6BZv6KDXTL/AUwU7enc73Z+Wq
        TKCPrUTSY3LyeesdCX2wGYxeOBKqp7Y5HJNOA38F54It3DtqNVlAQyZ/pdDRatJg
        xCjLdSpXMNoTYXmB9fZZ7zpDRyG1hoZOeDqGnIoNAoGBAKAZyhzU6fH/IUx6/aVI
        3gIpC4M/271TKn1+sDt8m2u27t4Ffj/B8Yzfa/KPJ4zM4vunVK25jKAlm+NMhtWD
        IaKkiRJj7Qym2jyp3mzUTonwjn2c3JSn9/l3vKTrKiUGfwgCT2X4Hf1UeSXEuJSs
        TSxB1MZrf3VHr0iM0zt9+so/
        -----END PRIVATE KEY-----
        """;
}
