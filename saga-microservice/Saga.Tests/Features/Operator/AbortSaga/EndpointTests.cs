using System.Net;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Tests.Authentication;

namespace Saga.Tests.Features.Operator.AbortSaga;

public class EndpointTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public EndpointTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_running_saga_When_abort_requested_Then_enters_compensation_and_dispatches_reverse_command()
    {
        var sagaId = await SeedSaga(OrderSagaStep.PaymentAuthorizing, SagaStatus.Running);

        using var client = CreateServiceClient();

        var response = await client.PostAsync($"/operator/api/sagas/{sagaId}/abort", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances.FindAsync(sagaId);

        Assert.NotNull(saga);
        Assert.Equal(SagaStatus.Compensating, saga.Status);
        Assert.Equal(OrderSagaStep.ReleasingStock.ToString(), saga.CurrentStep);
        Assert.NotNull(saga.LastCommandId);

        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var pending = await store.GetUnpublishedOutboxEvents();
        Assert.Contains(pending, row =>
            row.Id == saga.LastCommandId
            && row.EventType.Contains(nameof(ReleaseStockCommand), StringComparison.Ordinal));
    }

    private HttpClient CreateServiceClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "service");
        return client;
    }

    private async Task<Guid> SeedSaga(
        OrderSagaStep step,
        SagaStatus status,
        Guid? lastCommandId = null,
        DateTime? nextTimeoutAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var saga = new SagaInstance
        {
            SagaId = sagaId,
            SagaType = "Order",
            CurrentStep = step.ToString(),
            Status = status,
            CorrelationId = Guid.NewGuid(),
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-1),
            NextTimeoutAt = status == SagaStatus.Running ? nextTimeoutAt ?? now.AddMinutes(4) : null,
            LastCommandId = lastCommandId,
            OrderSagaState = new OrderSagaState
            {
                SagaId = sagaId,
                OrderId = orderId,
                LastStepResult = lastCommandId is null ? null : nameof(ReserveStockCommand),
                Amount = 10m
            },
            Transitions =
            {
                new SagaTransition
                {
                    SagaId = sagaId,
                    FromStep = OrderSagaStep.Started.ToString(),
                    ToStep = step.ToString(),
                    Timestamp = now.AddMinutes(-1),
                    TriggerMessageId = Guid.NewGuid(),
                    TriggerKind = SagaTriggerKind.Event
                }
            }
        };

        sagaContext.SagaInstances.Add(saga);
        await sagaContext.SaveChangesAsync();

        return sagaId;
    }
}
