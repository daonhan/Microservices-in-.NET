using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.EntityFrameworkCore.Storage;
using Order.Service.Contracts.Integration;
using Order.Service.Domain.Events;
using Order.Service.Features.CreateOrder;
using Order.Service.Infrastructure.Outbox;
using Order.Service.Infrastructure.Outbox.Mappers;

namespace Order.Tests.Infrastructure.Outbox;

public class DomainEventOutboxInterceptorTests
{
    [Fact]
    public async Task PublishAsync_WithMultipleMappedDomainEvents_EmitsOneOutboxEventPerDomainEventWithMappedPayload()
    {
        var outboxStore = new CapturingOutboxStore();
        var interceptor = new DomainEventOutboxInterceptor(
            new IIntegrationMap[]
            {
                new OrderCreatedIntegrationMap(),
                new OrderConfirmedIntegrationMap(),
                new OrderCancelledIntegrationMap()
            },
            outboxStore);

        var orderId = Guid.NewGuid();
        const string customerId = "cust-42";
        var created = new OrderCreatedDomainEvent(
            orderId,
            customerId,
            [new OrderItemSnapshot("p-1", 2, 9.99m), new OrderItemSnapshot("p-2", 1, 4.50m)],
            "EUR");
        var confirmed = new OrderConfirmedDomainEvent(orderId, customerId);
        var cancelled = new OrderCancelledDomainEvent(orderId, customerId);

        await interceptor.PublishAsync([created, confirmed, cancelled]);

        Assert.Equal(3, outboxStore.Events.Count);

        var createdEvent = Assert.IsType<OrderCreatedEvent>(outboxStore.Events[0]);
        Assert.Equal(orderId, createdEvent.OrderId);
        Assert.Equal(customerId, createdEvent.CustomerId);
        Assert.Equal("EUR", createdEvent.Currency);
        Assert.Equal(2, createdEvent.Items.Count);
        Assert.Contains(createdEvent.Items, i => i.ProductId == "p-1" && i.Quantity == 2 && i.UnitPrice == 9.99m);
        Assert.Contains(createdEvent.Items, i => i.ProductId == "p-2" && i.Quantity == 1 && i.UnitPrice == 4.50m);

        var confirmedEvent = Assert.IsType<OrderConfirmedEvent>(outboxStore.Events[1]);
        Assert.Equal(orderId, confirmedEvent.OrderId);
        Assert.Equal(customerId, confirmedEvent.CustomerId);

        var cancelledEvent = Assert.IsType<OrderCancelledEvent>(outboxStore.Events[2]);
        Assert.Equal(orderId, cancelledEvent.OrderId);
        Assert.Equal(customerId, cancelledEvent.CustomerId);
    }

    [Fact]
    public async Task PublishAsync_WithUnmappedDomainEventType_ThrowsInvalidOperationExceptionNamingTheType()
    {
        var outboxStore = new CapturingOutboxStore();
        var interceptor = new DomainEventOutboxInterceptor(
            [new OrderCreatedIntegrationMap()],
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
