using System.Net;
using System.Net.Http.Json;
using Order.Service.Features.ListOrders;

namespace Order.Tests.Features.ListOrders;

public class ListOrdersEndpointTests : IntegrationTestBase
{
    public ListOrdersEndpointTests(OrderWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task ListOrders_WhenNoOrdersExist_ThenReturnsEmptyList()
    {
        // Arrange — use a customerId unlikely to have existing data
        var customerId = $"list-empty-{Guid.NewGuid()}";

        // Act
        var response = await HttpClient.GetAsync($"/{customerId}");

        // Assert
        response.EnsureSuccessStatusCode();

        var orders = await response.Content.ReadFromJsonAsync<List<ListOrdersResponse>>();

        Assert.NotNull(orders);
        Assert.Empty(orders);
    }

    [Fact]
    public async Task ListOrders_WhenOrdersExist_ThenReturnsAllCustomerOrders()
    {
        // Arrange
        var customerId = $"list-multi-{Guid.NewGuid()}";

        var order1 = new Service.Domain.Order { CustomerId = customerId };
        var order2 = new Service.Domain.Order { CustomerId = customerId };

        await OrderContext.CreateOrder(order1);
        await OrderContext.CreateOrder(order2);
        await OrderContext.SaveChangesAsync();

        // Act
        var response = await HttpClient.GetAsync($"/{customerId}");

        // Assert
        response.EnsureSuccessStatusCode();

        var orders = await response.Content.ReadFromJsonAsync<List<ListOrdersResponse>>();

        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.Equal(customerId, o.CustomerId));
    }

    [Fact]
    public async Task ListOrders_DoesNotReturnOtherCustomersOrders()
    {
        // Arrange
        var targetCustomerId = $"list-target-{Guid.NewGuid()}";
        var otherCustomerId = $"list-other-{Guid.NewGuid()}";

        var targetOrder = new Service.Domain.Order { CustomerId = targetCustomerId };
        var otherOrder = new Service.Domain.Order { CustomerId = otherCustomerId };

        await OrderContext.CreateOrder(targetOrder);
        await OrderContext.CreateOrder(otherOrder);
        await OrderContext.SaveChangesAsync();

        // Act
        var response = await HttpClient.GetAsync($"/{targetCustomerId}");

        // Assert
        response.EnsureSuccessStatusCode();

        var orders = await response.Content.ReadFromJsonAsync<List<ListOrdersResponse>>();

        Assert.NotNull(orders);
        Assert.Single(orders);
        Assert.Equal(targetOrder.OrderId, orders[0].OrderId);
    }
}
