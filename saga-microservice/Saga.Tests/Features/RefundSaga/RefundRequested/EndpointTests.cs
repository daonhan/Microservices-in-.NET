using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Contracts.Integration.InboundEvents;
using Saga.Service.Domain;
using Saga.Service.Domain.RefundSaga;
using Saga.Service.Features.RefundSaga.RefundRequested;
using Saga.Service.Infrastructure.Data.EntityFramework;

namespace Saga.Tests.Features.RefundSaga.RefundRequested;

public class EndpointTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public EndpointTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_RefundRequested_When_HandlerRuns_Then_SagaOpensAndRefundCommandQueued()
    {
        var orderId = Guid.NewGuid();
        var requested = CreateRefundRequested(orderId, shipmentId: Guid.NewGuid());

        await OpenSaga(requested);

        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.RefundSagaState)
            .Include(s => s.Transitions)
            .SingleAsync(s => s.RefundSagaState!.OrderId == orderId);

        Assert.Equal("Refund", saga.SagaType);
        Assert.Equal(RefundSagaStep.PaymentRefunding.ToString(), saga.CurrentStep);
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Equal(requested.PaymentId, saga.RefundSagaState!.PaymentId);
        var transition = Assert.Single(saga.Transitions);
        Assert.Equal(RefundSagaStep.Started.ToString(), transition.FromStep);
        Assert.Equal(RefundSagaStep.PaymentRefunding.ToString(), transition.ToStep);

        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(outboxEvents, e =>
            e.EventType.Contains(nameof(RefundPaymentCommand), StringComparison.Ordinal)
            && e.Data.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task Handle(RefundRequestedEvent requested)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<RefundRequestedHandler>(scope.ServiceProvider);

        await handler.Handle(requested);
    }

    private async Task<Guid> OpenSaga(RefundRequestedEvent requested)
    {
        await Handle(requested);

        using var scope = _factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        return outboxEvents.Single(e =>
            e.EventType.Contains(nameof(RefundPaymentCommand), StringComparison.Ordinal)
            && e.Data.Contains(requested.OrderId.ToString(), StringComparison.Ordinal)).Id;
    }

    private static RefundRequestedEvent CreateRefundRequested(Guid orderId, Guid? shipmentId) =>
        new(orderId, Guid.NewGuid(), shipmentId, 42.50m, "USD")
        {
            CorrelationId = Guid.NewGuid()
        };
}
