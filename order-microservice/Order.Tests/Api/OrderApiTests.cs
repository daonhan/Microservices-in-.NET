using System.Net;
using System.Net.Http.Json;
using Order.Service.ApiModels;
using Order.Service.IntegrationEvents.Events;

namespace Order.Tests.Api;

public class OrderApiTests : IntegrationTestBase
{
    public OrderApiTests(OrderWebApplicationFactory webApplicationFactory)
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
        var order = new Service.Models.Order { CustomerId = "1" };
        await OrderContext.CreateOrder(order);

        // Act
        var response = await HttpClient.GetAsync($"/{order.CustomerId}/{order.OrderId}");

        // Assert
        response.EnsureSuccessStatusCode();

        var getOrderResponse = await response.Content.ReadFromJsonAsync<GetOrderResponse>();

        Assert.NotNull(getOrderResponse);
        Assert.Equal(order.OrderId, getOrderResponse.OrderId);
    }

    [Fact]
    public async Task CreateOrder_WhenCalled_ThenCreatesOrder()
    {
        // Arrange
        var createOrderRequest = new CreateOrderRequest([]);

        // Act
        var response = await HttpClient.PostAsJsonAsync("/1", createOrderRequest);

        // Assert
        response.EnsureSuccessStatusCode();

        var locationHeader = response.Headers.FirstOrDefault(h =>
            string.Equals(h.Key, "Location", StringComparison.Ordinal)).Value.FirstOrDefault();

        Assert.NotNull(locationHeader);

        var split = locationHeader.Split('/');
        var customerId = split[0];
        var orderId = split[1];

        var order = OrderContext.Orders.FirstOrDefault(o =>
            o.OrderId == Guid.Parse(orderId) && o.CustomerId == customerId);

        Assert.NotNull(order);
    }

    [Fact]
    public async Task CreateOrder_WhenCalled_ThenOrderCreatedEventPublished()
    {
        // Arrange
        const string customerId = "1";
        var createOrderRequest = new CreateOrderRequest([]);

        Subscribe<OrderCreatedEvent>();

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/{customerId}", createOrderRequest);

        // Assert
        response.EnsureSuccessStatusCode();

        SpinWait.SpinUntil(() => ReceivedEvents.Count > 0, TimeSpan.FromSeconds(5));

        Assert.NotEmpty(ReceivedEvents);

        var receivedEvent = ReceivedEvents.First();
        Assert.IsType<OrderCreatedEvent>(receivedEvent);
        Assert.Equal(customerId, (receivedEvent as OrderCreatedEvent)!.CustomerId);
    }
}
