using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.Contracts.Integration;

namespace Shipping.Tests.Api;

public class InternalOutboxEndpointsTests : IntegrationTestBase
{
    public InternalOutboxEndpointsTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Given_no_credentials_When_GET_failed_outbox_Then_Unauthorized()
    {
        var response = await HttpClient.GetAsync("/internal/outbox/failed");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_non_service_role_When_GET_failed_outbox_Then_Forbidden()
    {
        var client = CreateAuthenticatedClient("Administrator");

        var response = await client.GetAsync("/internal/outbox/failed");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Given_service_role_When_GET_failed_outbox_Then_returns_failed_rows()
    {
        var failedId = await SeedFailedOutboxEventAsync();

        var client = CreateAuthenticatedClient("service");

        var response = await client.GetAsync("/internal/outbox/failed");

        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(rows);
        Assert.Contains(rows, r => r.GetProperty("id").GetGuid() == failedId);
        var row = rows.First(r => r.GetProperty("id").GetGuid() == failedId);
        Assert.Equal("boom", row.GetProperty("lastError").GetString());
        Assert.True(row.GetProperty("attempts").GetInt32() >= 1);
    }

    private async Task<Guid> SeedFailedOutboxEventAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        var evt = new ShipmentCreatedEvent(
            ShipmentId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: "test",
            WarehouseId: 1,
            Lines: Array.Empty<ShipmentLineItem>());

        await store.AddOutboxEvent(evt);
        await store.RecordPublishFailure(evt.Id, "boom", maxAttempts: 1);

        return evt.Id;
    }
}
