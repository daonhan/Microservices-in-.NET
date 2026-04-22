using System.Net;
using System.Text;

namespace ApiGateway.Tests.Integration;

public class GatewayIntegrationTests : IAsyncLifetime, IDisposable
{
    private StubHttpServer _authStub = null!;

    public async Task InitializeAsync()
    {
        _authStub = new StubHttpServer();
        await _authStub.StartAsync();
    }

    public async Task DisposeAsync() => await _authStub.DisposeAsync();

    public void Dispose() => GC.SuppressFinalize(this);

    [Theory]
    [InlineData("Ocelot")]
    [InlineData("Yarp")]
    public async Task PostLogin_ProxiesToAuthStub_Returns200(string provider)
    {
        await using var harness = await GatewayTestHarness.CreateAsync(provider, _authStub.BaseUrl);

        var response = await harness.Client.PostAsync(
            "/login",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_authStub.ReceivedRequests, r => r.Method == "POST" && r.Path == "/login");
    }

    [Fact]
    public async Task Yarp_UnmatchedRoute_Returns404()
    {
        await using var harness = await GatewayTestHarness.CreateAsync("Yarp", _authStub.BaseUrl);

        var response = await harness.Client.GetAsync("/product/123");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
