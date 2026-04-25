using System.Net;

namespace Payment.Tests.Api;

public class HealthEndpointTests : IntegrationTestBase
{
    public HealthEndpointTests(PaymentWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Given_PaymentService_When_HealthLiveCalled_Then_Returns200()
    {
        var response = await HttpClient.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
