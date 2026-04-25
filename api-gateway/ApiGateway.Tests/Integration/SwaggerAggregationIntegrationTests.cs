using System.Net;
using System.Text.Json.Nodes;

namespace ApiGateway.Tests.Integration;

public class SwaggerAggregationIntegrationTests : IAsyncLifetime, IDisposable
{
    private SwaggerStubServer _stub = null!;

    public async Task InitializeAsync()
    {
        _stub = new SwaggerStubServer();
        await _stub.StartAsync();
    }

    public async Task DisposeAsync() => await _stub.DisposeAsync();

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact]
    public async Task SwaggerUi_InDevelopment_Returns200Html()
    {
        await using var harness = await GatewayTestHarness.CreateAsync("Yarp", _stub.BaseUrl, "Development");

        var response = await harness.Client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        Assert.StartsWith("text/html", contentType, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("product")]
    [InlineData("basket")]
    [InlineData("order")]
    [InlineData("inventory")]
    [InlineData("shipping")]
    [InlineData("payment")]
    public async Task ServiceSpec_InDevelopment_Returns200Json(string serviceTag)
    {
        await using var harness = await GatewayTestHarness.CreateAsync("Yarp", _stub.BaseUrl, "Development");

        var response = await harness.Client.GetAsync($"/swagger/{serviceTag}/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        Assert.StartsWith("application/json", contentType, StringComparison.Ordinal);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonNode.Parse(body)!.AsObject();
        Assert.True(doc.ContainsKey("paths"));
    }

    [Fact]
    public async Task ProductSpec_DropsUnroutedOperations_AndRewritesPaths()
    {
        await using var harness = await GatewayTestHarness.CreateAsync("Yarp", _stub.BaseUrl, "Development");

        var response = await harness.Client.GetAsync("/swagger/product/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var paths = JsonNode.Parse(body)!.AsObject()["paths"]!.AsObject();

        Assert.True(paths.ContainsKey("/product/{id}"));
        Assert.True(paths.ContainsKey("/product"));
        Assert.False(paths.ContainsKey("/internal/admin-only"));
        Assert.False(paths.ContainsKey("/product/internal/admin-only"));
    }

    [Fact]
    public async Task SwaggerUi_InProduction_Returns404()
    {
        await using var harness = await GatewayTestHarness.CreateAsync("Yarp", _stub.BaseUrl, "Production");

        var response = await harness.Client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/swagger/auth/v1/swagger.json")]
    [InlineData("/swagger/product/v1/swagger.json")]
    [InlineData("/swagger/basket/v1/swagger.json")]
    [InlineData("/swagger/order/v1/swagger.json")]
    [InlineData("/swagger/inventory/v1/swagger.json")]
    [InlineData("/swagger/shipping/v1/swagger.json")]
    [InlineData("/swagger/payment/v1/swagger.json")]
    public async Task ServiceSpec_InProduction_Returns404(string path)
    {
        await using var harness = await GatewayTestHarness.CreateAsync("Yarp", _stub.BaseUrl, "Production");

        var response = await harness.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
