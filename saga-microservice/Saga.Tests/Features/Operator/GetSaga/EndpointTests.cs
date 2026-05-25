using System.Net.Http.Json;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Features.Operator.GetSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Tests.Authentication;

namespace Saga.Tests.Features.Operator.GetSaga;

public class EndpointTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public EndpointTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_saga_with_transitions_When_detail_requested_Then_returns_history()
    {
        var sagaId = await SeedSaga(OrderSagaStep.PaymentAuthorizing, SagaStatus.Running);

        using var client = CreateServiceClient();

        var response = await client.GetAsync($"/operator/api/sagas/{sagaId}");

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<GetSagaResponse>();
        Assert.NotNull(detail);
        Assert.Equal(sagaId, detail.SagaId);
        Assert.NotNull(detail.Order);
        Assert.NotEmpty(detail.Transitions);
        Assert.Contains(detail.Transitions, transition =>
            transition.FromStep == OrderSagaStep.Started.ToString()
            && transition.ToStep == OrderSagaStep.PaymentAuthorizing.ToString());
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
