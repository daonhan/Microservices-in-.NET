using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Order.Service.ApiModels;
using Order.Service.Endpoints;
using Order.Service.Infrastructure.Data;
using Order.Service.Models;

namespace Order.Tests.Api;

public class OrderApiEndpointTests
{
    private sealed class CapturingOrderStore : IOrderStore
    {
        public Service.Models.Order? Captured { get; private set; }

        public Task CreateOrder(Service.Models.Order order)
        {
            Captured = order;
            return Task.CompletedTask;
        }

        public Task<Service.Models.Order?> GetCustomerOrderById(string customerId, string orderId) => Task.FromResult<Service.Models.Order?>(null);
        public Task<Service.Models.Order?> GetOrderById(Guid orderId) => Task.FromResult<Service.Models.Order?>(null);
        public Task ExecuteAsync(Func<Task> unitOfWork) => unitOfWork();
    }

    [Fact]
    public async Task CreateOrder_FetchesPricesFromProvider()
    {
        var orderStore = new CapturingOrderStore();
        var priceProvider = new Mock<IProductPriceProvider>();
        var metricFactory = new MetricFactory("TestMeter");

        var request = new CreateOrderRequest([new OrderProductDto("prod1", 2)]);
        var customerId = "cust1";

        priceProvider.Setup(p => p.GetUnitPricesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, decimal> { { "prod1", 10.5m } });

        var result = await OrderApiEndpoint.CreateOrder(orderStore, priceProvider.Object, metricFactory, customerId, request);

        Assert.IsType<Created>(result);
        priceProvider.Verify(p => p.GetUnitPricesAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("prod1"))), Times.Once);
    }

    [Fact]
    public async Task CreateOrder_RaisesOrderCreatedDomainEventWithItemsAndPrices()
    {
        var orderStore = new CapturingOrderStore();
        var priceProvider = new Mock<IProductPriceProvider>();
        priceProvider.Setup(p => p.GetUnitPricesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, decimal> { { "prod1", 10.5m }, { "prod2", 4m } });

        var metricFactory = new MetricFactory("TestMeter");
        var request = new CreateOrderRequest([
            new OrderProductDto("prod1", 2),
            new OrderProductDto("prod2", 1)
        ]);

        await OrderApiEndpoint.CreateOrder(orderStore, priceProvider.Object, metricFactory, "cust1", request);

        Assert.NotNull(orderStore.Captured);
        var domainEvent = Assert.IsType<OrderCreatedDomainEvent>(Assert.Single(orderStore.Captured!.DequeueDomainEvents()));
        Assert.Equal(orderStore.Captured.OrderId, domainEvent.OrderId);
        Assert.Equal("cust1", domainEvent.CustomerId);
        Assert.Equal(2, domainEvent.Items.Count);
        Assert.Contains(domainEvent.Items, i => i.ProductId == "prod1" && i.Quantity == 2 && i.UnitPrice == 10.5m);
        Assert.Contains(domainEvent.Items, i => i.ProductId == "prod2" && i.Quantity == 1 && i.UnitPrice == 4m);
    }
}
