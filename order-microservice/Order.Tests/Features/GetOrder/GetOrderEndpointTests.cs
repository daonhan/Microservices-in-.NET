using System.Net;
using System.Net.Http.Json;
using Order.Service.Features.GetOrder;

namespace Order.Tests.Features.GetOrder;

public class GetOrderEndpointTests : IntegrationTestBase
{
    public GetOrderEndpointTests(OrderWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetOrder_WhenNoOrderExists_ThenReturnsNotFound()
    {
        // Act
        var response = await HttpClient.GetAsync($"/1/{Guid.NewGuid()}");

        // Assert
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            Console.WriteLine(await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOrder_WhenOrderExists_ThenReturnsOrder()
    {
        // Arrange
        var order = new Service.Domain.Order { CustomerId = "1" };
        await OrderContext.CreateOrder(order);
        await OrderContext.SaveChangesAsync();

        // Act
        var response = await HttpClient.GetAsync($"/{order.CustomerId}/{order.OrderId}");

        // Assert
        response.EnsureSuccessStatusCode();

        var getOrderResponse = await response.Content.ReadFromJsonAsync<GetOrderResponse>();

        Assert.NotNull(getOrderResponse);
        Assert.Equal(order.OrderId, getOrderResponse.OrderId);
    }
}
