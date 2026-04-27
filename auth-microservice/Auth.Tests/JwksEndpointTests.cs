using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using Auth.Service.Endpoints;
using Auth.Service.Services.Signing;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Tests;

public class JwksEndpointTests : IDisposable
{
    private readonly MetricFactory _metricFactory = new("Auth.Tests");

    public void Dispose()
    {
        _metricFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetJwks_WhenInvoked_ThenReturnsActiveKeyAsRfc7517Jwks()
    {
        // Arrange
        using var rsa = RSA.Create(2048);
        var provider = new StubRsaKeyProvider("active-key-id", rsa);
        var httpContext = new DefaultHttpContext();

        // Act
        var result = JwksEndpoint.GetJwks(provider, _metricFactory, httpContext);

        // Assert
        Assert.Equal("public, max-age=300", httpContext.Response.Headers.CacheControl.ToString());
        Assert.NotNull(result.Value);
        Assert.Single(result.Value!.keys);

        var key = result.Value.keys[0];
        Assert.Equal("active-key-id", key.kid);
        Assert.Equal("RSA", key.kty);
        Assert.Equal("sig", key.use);
        Assert.Equal(SecurityAlgorithms.RsaSha256, key.alg);

        var modulusBytes = Base64UrlEncoder.DecodeBytes(key.n);
        var exponentBytes = Base64UrlEncoder.DecodeBytes(key.e);

        using var rebuilt = RSA.Create();
        rebuilt.ImportParameters(new RSAParameters
        {
            Modulus = modulusBytes,
            Exponent = exponentBytes
        });

        var expected = rsa.ExportParameters(includePrivateParameters: false);
        Assert.Equal(expected.Modulus, modulusBytes);
        Assert.Equal(expected.Exponent, exponentBytes);
    }

    [Fact]
    public void GetJwks_WhenPreviousKeysConfigured_ThenIncludesEachKey()
    {
        // Arrange
        using var active = RSA.Create(2048);
        using var previous = RSA.Create(2048);
        var provider = new StubRsaKeyProvider(
            "active-key",
            active,
            (KeyId: "previous-key", PublicKey: previous));
        var httpContext = new DefaultHttpContext();

        // Act
        var result = JwksEndpoint.GetJwks(provider, _metricFactory, httpContext);

        // Assert
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value!.keys.Length);
        Assert.Equal("active-key", result.Value.keys[0].kid);
        Assert.Equal("previous-key", result.Value.keys[1].kid);
    }

    [Fact]
    public void GetJwks_WhenInvoked_ThenEmitsJwksServedCounter()
    {
        // Arrange
        using var rsa = RSA.Create(2048);
        var provider = new StubRsaKeyProvider("k1", rsa);
        var httpContext = new DefaultHttpContext();

        var observed = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Auth.Tests")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, _, _, _) =>
            observed.Add(instrument.Name));
        listener.Start();

        // Act
        JwksEndpoint.GetJwks(provider, _metricFactory, httpContext);

        // Assert
        Assert.Contains("jwks-served", observed);
    }

    private sealed class StubRsaKeyProvider : IRsaKeyProvider
    {
        private readonly RSA _privateKey;
        private readonly List<PublishedKey> _published;

        public StubRsaKeyProvider(string activeKeyId, RSA privateKey,
            params (string KeyId, RSA PublicKey)[] previous)
        {
            ActiveKeyId = activeKeyId;
            _privateKey = privateKey;

            var activePublic = RSA.Create();
            activePublic.ImportParameters(privateKey.ExportParameters(includePrivateParameters: false));

            _published = new List<PublishedKey> { new(activeKeyId, activePublic) };
            foreach (var p in previous)
            {
                _published.Add(new PublishedKey(p.KeyId, p.PublicKey));
            }
        }

        public string ActiveKeyId { get; }
        public RSA GetActivePrivateKey() => _privateKey;
        public IReadOnlyList<PublishedKey> GetPublishedPublicKeys() => _published;
    }
}
