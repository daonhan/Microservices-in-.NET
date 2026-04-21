using System.Diagnostics.Metrics;
using Auth.Service.ApiModels;
using Auth.Service.Endpoints;
using Auth.Service.Models;
using Auth.Service.Services;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;

namespace Auth.Tests;

public class AuthApiEndpointsTests : IDisposable
{
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly MetricFactory _metricFactory = new("Auth.Tests");

    public void Dispose()
    {
        _metricFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Login_WhenCredentialsValid_ThenReturnsOkAndEmitsLoginSuccessCounter()
    {
        // Arrange
        var loginRequest = new LoginRequest("alice", "password");
        _tokenService.GenerateAuthenticationToken("alice", "password")
            .Returns(new AuthToken("token", 900));

        var observed = CaptureCounters();

        // Act
        var result = await AuthApiEndpoints.Login(_tokenService, _metricFactory, loginRequest);

        // Assert
        Assert.IsType<Ok<AuthToken>>(result.Result);
        Assert.Contains("login-success", observed);
        Assert.DoesNotContain("login-failure", observed);
    }

    [Fact]
    public async Task Login_WhenCredentialsInvalid_ThenReturnsUnauthorizedAndEmitsLoginFailureCounter()
    {
        // Arrange
        var loginRequest = new LoginRequest("alice", "bad");
        _tokenService.GenerateAuthenticationToken("alice", "bad")
            .Returns((AuthToken?)null);

        var observed = CaptureCounters();

        // Act
        var result = await AuthApiEndpoints.Login(_tokenService, _metricFactory, loginRequest);

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
