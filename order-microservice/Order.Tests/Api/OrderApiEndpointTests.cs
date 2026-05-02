using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Order.Service.ApiModels;
using Order.Service.Endpoints;
using Order.Service.Infrastructure.Data;
using Order.Service.Models;
using System.Diagnostics.Metrics;

namespace Order.Tests.Api;

public class OrderApiEndpointTests
{
    private sealed class FakeExecutionStrategy : IExecutionStrategy
    {
        public bool RetriesOnFailure => false;

        public TResult Execute<TState, TResult>(TState state, Func<DbContext, TState, TResult> operation, Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
        {
            return operation(null!, state);
        }

        public Task<TResult> ExecuteAsync<TState, TResult>(TState state, Func<DbContext, TState, CancellationToken, Task<TResult>> operation, Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded, CancellationToken cancellationToken = default)
        {
            return operation(null!, state, cancellationToken);
        }
    }

    private sealed class FakeOutboxStore : IOutboxStore
    {
        Task IOutboxStore.AddOutboxEvent<T>(T outboxEvent) => Task.CompletedTask;
        IExecutionStrategy IOutboxStore.CreateExecutionStrategy() => new FakeExecutionStrategy();
        Task<List<ECommerce.Shared.Infrastructure.Outbox.Models.OutboxEvent>> IOutboxStore.GetUnpublishedOutboxEvents() => throw new NotImplementedException();
        Task IOutboxStore.MarkOutboxEventAsPublished(Guid id) => throw new NotImplementedException();
    }

    private sealed class FakeOrderStore : IOrderStore
    {
        public Task CreateOrder(Order.Service.Models.Order order) => Task.CompletedTask;
        public Task<Order.Service.Models.Order?> GetCustomerOrderById(string customerId, string orderId) => Task.FromResult<Order.Service.Models.Order?>(null);
        public Task<Order.Service.Models.Order?> GetOrderById(Guid orderId) => Task.FromResult<Order.Service.Models.Order?>(null);
        public Task ExecuteAsync(Func<Task> unitOfWork) => unitOfWork();
    }

    [Fact]
    public async Task CreateOrder_FetchesPricesFromProvider()
    {
        // Arrange
        var outboxStore = new FakeOutboxStore();
        var orderStore = new FakeOrderStore();
        var priceProvider = new Mock<IProductPriceProvider>();
        
        var metricFactory = new MetricFactory("TestMeter");
        
        var request = new CreateOrderRequest(new List<OrderProductDto> { new OrderProductDto("prod1", 2) });
        var customerId = "cust1";

        priceProvider.Setup(p => p.GetUnitPricesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, decimal> { { "prod1", 10.5m } });

        // Act
        var result = await OrderApiEndpoint.CreateOrder(outboxStore, orderStore, priceProvider.Object, metricFactory, customerId, request);

        // Assert
        Assert.IsType<Created>(result);
        priceProvider.Verify(p => p.GetUnitPricesAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("prod1"))), Times.Once);
    }
}
