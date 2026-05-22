using Auth.Service.Domain;
using Auth.Service.Domain.Tokens;
using Auth.Service.Features.IssueServiceToken;
using ECommerce.Shared.Observability.Metrics;
using NSubstitute;

namespace Auth.Tests.Features.IssueServiceToken;

public class IssueServiceTokenHandlerTests : IDisposable
{
    private readonly IServiceTokenService _serviceTokenService = Substitute.For<IServiceTokenService>();
    private readonly MetricFactory _metricFactory = new("Auth.Tests");

    public void Dispose()
    {
        _metricFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private IssueServiceTokenHandler BuildHandler() => new(
        new ServiceClientOptions
        {
            Clients = [new ServiceClient { ClientId = "api-gateway", ClientSecret = "s3cret" }]
        },
        _serviceTokenService,
        _metricFactory);

    [Fact]
    public void Given_unknown_client_id_When_handling_Then_returns_null()
    {
        var result = BuildHandler().Handle("unknown-client", "s3cret");

        Assert.Null(result);
        _serviceTokenService.DidNotReceiveWithAnyArgs().GenerateServiceToken(default!);
    }

    [Fact]
    public void Given_wrong_client_secret_When_handling_Then_returns_null()
    {
        var result = BuildHandler().Handle("api-gateway", "wrong");

        Assert.Null(result);
        _serviceTokenService.DidNotReceiveWithAnyArgs().GenerateServiceToken(default!);
    }

    [Fact]
    public void Given_valid_client_credentials_When_handling_Then_returns_auth_token()
    {
        _serviceTokenService.GenerateServiceToken("api-gateway")
            .Returns(new AuthToken("token", 900));

        var result = BuildHandler().Handle("api-gateway", "s3cret");

        Assert.NotNull(result);
        Assert.Equal("token", result.Token);
    }
}
