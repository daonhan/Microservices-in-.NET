using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.EntityFrameworkCore.Storage;
using Product.Service.Contracts.Integration;
using Product.Service.Domain.Events;
using Product.Service.Features.CreateProduct;
using Product.Service.Features.UpdateProduct;
using Product.Service.Infrastructure.Outbox;

namespace Product.Tests.Infrastructure.Outbox;

public class DomainEventOutboxInterceptorTests
{
    [Fact]
    public async Task PublishAsync_WithMultipleMappedDomainEvents_EmitsOneOutboxEventPerDomainEventWithMappedPayload()
    {
        var outboxStore = new CapturingOutboxStore();
        var interceptor = new DomainEventOutboxInterceptor(
            new IIntegrationMap[]
            {
                new ProductCreatedIntegrationMap(),
                new ProductPriceUpdatedIntegrationMap()
            },
            outboxStore);

        var product = new Product.Service.Domain.Product("Test Shoe", 49.99m, 1, "A test shoe");
        var created = new ProductCreatedDomainEvent(product);
        var priceChanged = new ProductPriceChangedDomainEvent(7, 75.00m);

        await interceptor.PublishAsync([created, priceChanged]);

        Assert.Equal(2, outboxStore.Events.Count);

        var createdEvent = Assert.IsType<ProductCreatedEvent>(outboxStore.Events[0]);
        Assert.Equal(product.Id, createdEvent.ProductId);
        Assert.Equal("Test Shoe", createdEvent.Name);
        Assert.Equal(49.99m, createdEvent.Price);

        var priceUpdatedEvent = Assert.IsType<ProductPriceUpdatedEvent>(outboxStore.Events[1]);
        Assert.Equal(7, priceUpdatedEvent.ProductId);
        Assert.Equal(75.00m, priceUpdatedEvent.NewPrice);
    }

    [Fact]
    public async Task PublishAsync_WithUnmappedDomainEventType_ThrowsInvalidOperationExceptionNamingTheType()
    {
        var outboxStore = new CapturingOutboxStore();
        var interceptor = new DomainEventOutboxInterceptor(
            [new ProductCreatedIntegrationMap()],
            outboxStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interceptor.PublishAsync([new UnmappedDomainEvent()]));

        Assert.Contains(nameof(UnmappedDomainEvent), ex.Message, StringComparison.Ordinal);
        Assert.Empty(outboxStore.Events);
    }

    private sealed record UnmappedDomainEvent : IDomainEvent;

    private sealed class CapturingOutboxStore : IOutboxStore
    {
        public List<Event> Events { get; } = [];

        public Task AddOutboxEvent<T>(T @event) where T : Event
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }

        public Task<List<OutboxEvent>> GetUnpublishedOutboxEvents() => throw new NotImplementedException();
        public Task MarkOutboxEventAsPublished(Guid outboxEventId) => throw new NotImplementedException();
        public Task<bool> RequeueOutboxEvent(Guid outboxEventId) => throw new NotImplementedException();
        public Task RecordPublishFailure(Guid outboxEventId, string error, int maxAttempts) => throw new NotImplementedException();
        public Task<List<OutboxEvent>> GetFailedOutboxEvents() => throw new NotImplementedException();
        public IExecutionStrategy CreateExecutionStrategy() => throw new NotImplementedException();
    }
}
