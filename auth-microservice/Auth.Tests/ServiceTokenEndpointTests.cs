using System.Diagnostics.Metrics;
using Auth.Service.Domain;
using Auth.Service.Domain.Tokens;
using Auth.Service.Endpoints;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;

namespace Auth.Tests;

public class ServiceTokenEndpointTests : IDisposable
{
    private readonly IServiceTokenService _serviceTokenService = Substitute.For<IServiceTokenService>();
    private readonly MetricFactory _metricFactory = new("Auth.Tests");

    public void Dispose()
    {
        _metricFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Given_valid_client_credentials_When_issuing_token_Then_returns_ok_and_emits_success_counter()
    {
        // Arrange
        _serviceTokenService.GenerateServiceToken("api-gateway", "s3cret")
            .Returns(new AuthToken("token", 900));
        var observed = CaptureCounters();

        // Act
        var result = ServiceTokenEndpoint.IssueToken(
            _serviceTokenService, _metricFactory, "client_credentials", "api-gateway", "s3cret");

        // Assert
        Assert.IsType<Ok<AuthToken>>(result.Result);
        Assert.Contains("service-token-success", observed);
        Assert.DoesNotContain("service-token-failure", observed);
    }

    [Fact]
    public void Given_invalid_client_credentials_When_issuing_token_Then_returns_unauthorized_and_emits_failure_counter()
    {
        // Arrange
        _serviceTokenService.GenerateServiceToken("api-gateway", "wrong")
            .Returns((AuthToken?)null);
        var observed = CaptureCounters();

        // Act
        var result = ServiceTokenEndpoint.IssueToken(
            _serviceTokenService, _metricFactory, "client_credentials", "api-gateway", "wrong");

        // Assert
        Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Contains("service-token-failure", observed);
        Assert.DoesNotContain("service-token-success", observed);
    }

    [Fact]
    public void Given_missing_client_id_When_issuing_token_Then_returns_unauthorized()
    {
        var result = ServiceTokenEndpoint.IssueToken(
            _serviceTokenService, _metricFactory, "client_credentials", null, "s3cret");

        Assert.IsType<UnauthorizedHttpResult>(result.Result);
        _serviceTokenService.DidNotReceiveWithAnyArgs().GenerateServiceToken(default!, default!);
    }

    [Fact]
    public void Given_missing_client_secret_When_issuing_token_Then_returns_unauthorized()
    {
        var result = ServiceTokenEndpoint.IssueToken(
            _serviceTokenService, _metricFactory, "client_credentials", "api-gateway", null);

        Assert.IsType<UnauthorizedHttpResult>(result.Result);
        _serviceTokenService.DidNotReceiveWithAnyArgs().GenerateServiceToken(default!, default!);
    }

    [Fact]
    public void Given_unsupported_grant_type_When_issuing_token_Then_returns_bad_request()
    {
        var result = ServiceTokenEndpoint.IssueToken(
            _serviceTokenService, _metricFactory, "password", "api-gateway", "s3cret");

        var badRequest = Assert.IsType<BadRequest<string>>(result.Result);
        Assert.Equal("unsupported_grant_type", badRequest.Value);
        _serviceTokenService.DidNotReceiveWithAnyArgs().GenerateServiceToken(default!, default!);
    }

    [Fact]
    public void Given_missing_grant_type_When_issuing_token_Then_returns_bad_request()
    {
        var result = ServiceTokenEndpoint.IssueToken(
            _serviceTokenService, _metricFactory, null, "api-gateway", "s3cret");

        Assert.IsType<BadRequest<string>>(result.Result);
        _serviceTokenService.DidNotReceiveWithAnyArgs().GenerateServiceToken(default!, default!);
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
