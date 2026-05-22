using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using Auth.Service.ApiModels;
using Auth.Service.Domain;
using Auth.Service.Domain.Abstractions;
using Auth.Service.Domain.Tokens;
using Auth.Service.Endpoints;
using Auth.Service.Services;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace Auth.Tests;

public class AuthApiEndpointsTests : IDisposable
{
    private readonly MetricFactory _metricFactory = new("Auth.Tests");
    private readonly RSA _rsa = RSA.Create(2048);

    public void Dispose()
    {
        _metricFactory.Dispose();
        _rsa.Dispose();
        GC.SuppressFinalize(this);
    }

    private LoginHandler BuildLoginHandler(IAuthStore authStore, IPasswordHasher<User> hasher)
    {
        var rsaKeyProvider = Substitute.For<IRsaKeyProvider>();
        rsaKeyProvider.GetActivePrivateKey().Returns(_rsa);
        rsaKeyProvider.ActiveKeyId.Returns("test-kid");
        var tokenService = new JwtTokenService(rsaKeyProvider,
            new AuthOptions { AuthMicroserviceBaseAddress = "http://localhost" });
        return new LoginHandler(authStore, hasher, tokenService);
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
        var result = await AuthApiEndpoints.Login(loginHandler, _metricFactory, loginRequest);

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
        var result = await AuthApiEndpoints.Login(loginHandler, _metricFactory, loginRequest);

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
                if (instrument.Meter.Name == "Auth.Tests")
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
