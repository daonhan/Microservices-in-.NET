using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ApiGateway.Tests.Integration;

public class GatewayIntegrationTests : IAsyncLifetime, IDisposable
{
    private StubHttpServer _downstreamStub = null!;

    public async Task InitializeAsync()
    {
        _downstreamStub = new StubHttpServer();
        await _downstreamStub.StartAsync();
    }

    public async Task DisposeAsync() => await _downstreamStub.DisposeAsync();

    public void Dispose() => GC.SuppressFinalize(this);

    [Theory]
    [InlineData("Ocelot")]
    [InlineData("Yarp")]
    public async Task PostLogin_ProxiesToAuthStub_Returns200(string provider)
    {
        await using var harness = await GatewayTestHarness.CreateAsync(provider, _downstreamStub.BaseUrl);

        var response = await harness.Client.PostAsync(
            "/login",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_downstreamStub.ReceivedRequests, r => r.Method == "POST" && r.Path == "/login");
    }

    [Fact]
    public async Task Yarp_UnmatchedRoute_Returns404()
    {
        await using var harness = await GatewayTestHarness.CreateAsync("Yarp", _downstreamStub.BaseUrl);

        var response = await harness.Client.GetAsync("/unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Ocelot")]
    [InlineData("Yarp")]
    public async Task GetProductById_Anonymous_ProxiesWithStrippedPrefix(string provider)
    {
        await using var harness = await GatewayTestHarness.CreateAsync(provider, _downstreamStub.BaseUrl);

        var response = await harness.Client.GetAsync("/product/42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_downstreamStub.ReceivedRequests, r => r.Method == "GET" && r.Path == "/42");
    }

    [Theory]
    [InlineData("Ocelot", "GET", "/basket/user-1", "/user-1")]
    [InlineData("Yarp", "GET", "/basket/user-1", "/user-1")]
    [InlineData("Ocelot", "POST", "/basket/user-1/items", "/user-1/items")]
    [InlineData("Yarp", "POST", "/basket/user-1/items", "/user-1/items")]
    [InlineData("Ocelot", "GET", "/order/123", "/123")]
    [InlineData("Yarp", "GET", "/order/123", "/123")]
    [InlineData("Ocelot", "POST", "/order", "/")]
    [InlineData("Yarp", "POST", "/order", "/")]
    [InlineData("Ocelot", "POST", "/inventory/42/backorder", "/42/backorder")]
    [InlineData("Yarp", "POST", "/inventory/42/backorder", "/42/backorder")]
    [InlineData("Ocelot", "GET", "/inventory/42", "/42")]
    [InlineData("Yarp", "GET", "/inventory/42", "/42")]
    public async Task AuthenticatedRoutes_WithValidToken_ProxyWithStrippedPrefix(
        string provider, string method, string upstreamPath, string expectedDownstreamPath)
    {
        await using var harness = await GatewayTestHarness.CreateAsync(provider, _downstreamStub.BaseUrl);
        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayTestHarness.CreateJwt());

        var response = await SendAsync(harness.Client, method, upstreamPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            _downstreamStub.ReceivedRequests,
            r => r.Method == method && r.Path == expectedDownstreamPath);
    }

    [Theory]
    [InlineData("Ocelot", "GET", "/basket/user-1")]
    [InlineData("Yarp", "GET", "/basket/user-1")]
    [InlineData("Ocelot", "POST", "/basket/user-1/items")]
    [InlineData("Yarp", "POST", "/basket/user-1/items")]
    [InlineData("Ocelot", "GET", "/order/123")]
    [InlineData("Yarp", "GET", "/order/123")]
    [InlineData("Ocelot", "POST", "/inventory/42/backorder")]
    [InlineData("Yarp", "POST", "/inventory/42/backorder")]
    [InlineData("Ocelot", "GET", "/inventory/42")]
    [InlineData("Yarp", "GET", "/inventory/42")]
    public async Task AuthenticatedRoutes_WithoutToken_Return401(
        string provider, string method, string upstreamPath)
    {
        await using var harness = await GatewayTestHarness.CreateAsync(provider, _downstreamStub.BaseUrl);

        var response = await SendAsync(harness.Client, method, upstreamPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Ocelot", "POST", "/product/42", "/42")]
    [InlineData("Yarp", "POST", "/product/42", "/42")]
    [InlineData("Ocelot", "PUT", "/product/42", "/42")]
    [InlineData("Yarp", "PUT", "/product/42", "/42")]
    [InlineData("Ocelot", "GET", "/inventory", "/")]
    [InlineData("Yarp", "GET", "/inventory", "/")]
    [InlineData("Ocelot", "POST", "/inventory/42", "/42")]
    [InlineData("Yarp", "POST", "/inventory/42", "/42")]
    [InlineData("Ocelot", "PUT", "/inventory/42", "/42")]
    [InlineData("Yarp", "PUT", "/inventory/42", "/42")]
    public async Task AdminRoutes_WithAdminToken_ProxyWithStrippedPrefix(
        string provider, string method, string upstreamPath, string expectedDownstreamPath)
    {
        await using var harness = await GatewayTestHarness.CreateAsync(provider, _downstreamStub.BaseUrl);
        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayTestHarness.CreateJwt(role: "Administrator"));

        var response = await SendAsync(harness.Client, method, upstreamPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            _downstreamStub.ReceivedRequests,
            r => r.Method == method && r.Path == expectedDownstreamPath);
    }

    [Theory]
    [InlineData("Ocelot", "POST", "/product/42")]
    [InlineData("Yarp", "POST", "/product/42")]
    [InlineData("Ocelot", "PUT", "/product/42")]
    [InlineData("Yarp", "PUT", "/product/42")]
    [InlineData("Ocelot", "GET", "/inventory")]
    [InlineData("Yarp", "GET", "/inventory")]
    [InlineData("Ocelot", "POST", "/inventory/42")]
    [InlineData("Yarp", "POST", "/inventory/42")]
    [InlineData("Ocelot", "PUT", "/inventory/42")]
    [InlineData("Yarp", "PUT", "/inventory/42")]
    public async Task AdminRoutes_WithoutToken_Return401(
        string provider, string method, string upstreamPath)
    {
        await using var harness = await GatewayTestHarness.CreateAsync(provider, _downstreamStub.BaseUrl);

        var response = await SendAsync(harness.Client, method, upstreamPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Ocelot", "POST", "/product/42")]
    [InlineData("Yarp", "POST", "/product/42")]
    [InlineData("Ocelot", "PUT", "/product/42")]
    [InlineData("Yarp", "PUT", "/product/42")]
    [InlineData("Ocelot", "GET", "/inventory")]
    [InlineData("Yarp", "GET", "/inventory")]
    [InlineData("Ocelot", "POST", "/inventory/42")]
    [InlineData("Yarp", "POST", "/inventory/42")]
    [InlineData("Ocelot", "PUT", "/inventory/42")]
    [InlineData("Yarp", "PUT", "/inventory/42")]
    public async Task AdminRoutes_WithNonAdminToken_Return403(
        string provider, string method, string upstreamPath)
    {
        await using var harness = await GatewayTestHarness.CreateAsync(provider, _downstreamStub.BaseUrl);
        harness.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayTestHarness.CreateJwt());

        var response = await SendAsync(harness.Client, method, upstreamPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }
        return client.SendAsync(request);
    }
}

