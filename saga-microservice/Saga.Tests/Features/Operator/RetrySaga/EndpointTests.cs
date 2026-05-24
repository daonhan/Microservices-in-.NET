using System.Net;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Tests.Authentication;

namespace Saga.Tests.Features.Operator.RetrySaga;

public class EndpointTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public EndpointTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_running_saga_When_retry_requested_Then_last_command_is_requeued()
    {
        var command = new ReserveStockCommand(
            Guid.NewGuid(),
            "customer",
            [new ReserveStockItem("9001", 1, 10m)],
            "USD",
            Guid.NewGuid(),
            Guid.NewGuid());
        await SeedFailedOutboxEvent(command);
        var sagaId = await SeedSaga(OrderSagaStep.StockReserving, SagaStatus.Running, command.Id);

        using var client = CreateServiceClient();

        var response = await client.PostAsync($"/operator/api/sagas/{sagaId}/retry", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var pending = await store.GetUnpublishedOutboxEvents();
        Assert.Contains(pending, row => row.Id == command.Id);

        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        var saga = await sagaContext.SagaInstances
            .Include(s => s.Transitions)
            .SingleAsync(s => s.SagaId == sagaId);
        Assert.NotNull(saga);
        Assert.Equal(0, saga.RetryCount);
        Assert.Contains(saga.Transitions, transition =>
            transition.TriggerKind == SagaTriggerKind.OperatorAction
            && transition.TriggerMessageId == command.Id);
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

    private async Task SeedFailedOutboxEvent(ReserveStockCommand command)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        await store.AddOutboxEvent(command);
        await store.RecordPublishFailure(command.Id, "operator retry test", maxAttempts: 1);
    }
}
