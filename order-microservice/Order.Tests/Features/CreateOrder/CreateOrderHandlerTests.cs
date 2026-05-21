using ECommerce.Shared.Observability.Metrics;
using Moq;
using Order.Service.Domain.Abstractions;
using Order.Service.Domain.Events;
using Order.Service.Features.CreateOrder;

namespace Order.Tests.Features.CreateOrder;

public class CreateOrderHandlerTests
{
    private sealed class CapturingOrderStore : IOrderStore
    {
        public Service.Domain.Order? Captured { get; private set; }

        public Task CreateOrder(Service.Domain.Order order)
        {
            Captured = order;
            return Task.CompletedTask;
        }

        public Task<Service.Domain.Order?> GetCustomerOrderById(string customerId, string orderId) => Task.FromResult<Service.Domain.Order?>(null);
        public Task<Service.Domain.Order?> GetOrderById(Guid orderId) => Task.FromResult<Service.Domain.Order?>(null);
        public Task ExecuteAsync(Func<Task> unitOfWork) => unitOfWork();
    }

    [Fact]
    public async Task HandleAsync_FetchesPricesFromProvider()
    {
        var orderStore = new CapturingOrderStore();
        var priceProvider = new Mock<IProductPriceProvider>();
        var metricFactory = new MetricFactory("TestMeter");

        var request = new CreateOrderRequest([new OrderProductDto("prod1", 2)]);
        const string customerId = "cust1";

        priceProvider.Setup(p => p.GetUnitPricesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, decimal> { { "prod1", 10.5m } });

        var handler = new CreateOrderHandler(orderStore, priceProvider.Object, metricFactory);
        var order = await handler.HandleAsync(customerId, request);

        Assert.NotNull(order);
        priceProvider.Verify(p => p.GetUnitPricesAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("prod1"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_RaisesOrderCreatedDomainEventWithItemsAndPrices()
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

        var handler = new CreateOrderHandler(orderStore, priceProvider.Object, metricFactory);
        await handler.HandleAsync("cust1", request);

        Assert.NotNull(orderStore.Captured);
        var domainEvent = Assert.IsType<OrderCreatedDomainEvent>(Assert.Single(orderStore.Captured!.DequeueDomainEvents()));
        Assert.Equal(orderStore.Captured.OrderId, domainEvent.OrderId);
        Assert.Equal("cust1", domainEvent.CustomerId);
        Assert.Equal(2, domainEvent.Items.Count);
        Assert.Contains(domainEvent.Items, i => i.ProductId == "prod1" && i.Quantity == 2 && i.UnitPrice == 10.5m);
        Assert.Contains(domainEvent.Items, i => i.ProductId == "prod2" && i.Quantity == 1 && i.UnitPrice == 4m);
    }
}
