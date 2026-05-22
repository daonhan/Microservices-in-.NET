using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Auth.Service.Domain;
using Auth.Service.Domain.Abstractions;
using Auth.Service.Domain.Tokens;
using Auth.Service.Features.Login;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace Auth.Tests.Features.Login;

public class LoginEndpointTests : IClassFixture<AuthWebApplicationFactory>, IDisposable
{
    private const string MeterName = "Auth.Tests.Features.Login.LoginEndpointTests";

    private readonly AuthWebApplicationFactory _factory;
    private readonly MetricFactory _metricFactory = new(MeterName);
    private readonly RSA _rsa = RSA.Create(2048);

    public LoginEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public void Dispose()
    {
        _metricFactory.Dispose();
        _rsa.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Login_WhenPostedOverHttp_ThenBindsJsonAndReturnsToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/login", new
        {
            Username = AuthWebApplicationFactory.LoginUsername,
            Password = AuthWebApplicationFactory.LoginPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<AuthToken>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.Token));
        Assert.True(token.ExpiresIn > 0);
    }

    private LoginHandler BuildLoginHandler(IAuthStore authStore, IPasswordHasher<User> hasher)
    {
        var rsaKeyProvider = Substitute.For<IRsaKeyProvider>();
        rsaKeyProvider.GetActivePrivateKey().Returns(_rsa);
        rsaKeyProvider.ActiveKeyId.Returns("test-kid");
        var tokenService = new JwtTokenService(rsaKeyProvider,
            new AuthOptions { AuthMicroserviceBaseAddress = "http://localhost" });
        return new LoginHandler(authStore, hasher, tokenService, _metricFactory);
    }

    [Fact]
    public async Task Login_WhenCredentialsValid_ThenReturnsOkAndEmitsLoginSuccessCounter()
    {
        // Arrange
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Username = "alice",
            Role = "User",
            PasswordHash = hasher.HashPassword(
                new User { Username = "", Role = "", PasswordHash = "" }, "password")
        };
        var authStore = Substitute.For<IAuthStore>();
        authStore.FindByUsernameAsync("alice").Returns(Task.FromResult<User?>(user));
        var loginHandler = BuildLoginHandler(authStore, hasher);
        var loginRequest = new LoginRequest("alice", "password");

        var observed = CaptureCounters();

        // Act
        var result = await LoginEndpoint.HandleAsync(loginHandler, loginRequest);

        // Assert
        Assert.IsType<Ok<AuthToken>>(result.Result);
        Assert.Contains("login-success", observed);
        Assert.DoesNotContain("login-failure", observed);
    }

    [Fact]
    public async Task Login_WhenCredentialsInvalid_ThenReturnsUnauthorizedAndEmitsLoginFailureCounter()
    {
        // Arrange
        var authStore = Substitute.For<IAuthStore>();
        authStore.FindByUsernameAsync("alice").Returns(Task.FromResult<User?>(null));
        var loginHandler = BuildLoginHandler(authStore, new PasswordHasher<User>());
        var loginRequest = new LoginRequest("alice", "bad");

        var observed = CaptureCounters();

        // Act
        var result = await LoginEndpoint.HandleAsync(loginHandler, loginRequest);

        // Assert
        Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Contains("login-failure", observed);
        Assert.DoesNotContain("login-success", observed);
    }

    private static List<string> CaptureCounters()
    {
        var observed = new List<string>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, _, _, _) =>
            observed.Add(instrument.Name));
        listener.Start();
        return observed;
    }
}
