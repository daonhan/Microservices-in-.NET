using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inventory.Tests;

public class HealthChecksTests : IntegrationTestBase
{
    public HealthChecksTests(InventoryWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Health_WhenCalled_ThenReturnsOk()
    {
        // Act
        var response = await HttpClient.GetAsync("/health");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task HealthLive_Returns200_WhenProcessIsUp()
    {
        var response = await HttpClient.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_Returns503_WhenProbeIsUnhealthy()
    {
        using var factory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHealthChecks()
                    .AddCheck("synthetic-failure",
                        () => HealthCheckResult.Unhealthy("forced failure"),
                        tags: ["ready"])));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
