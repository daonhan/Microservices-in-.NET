using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Features.Operator.ListSagas;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Tests.Authentication;

namespace Saga.Tests.Features.Operator.ListSagas;

public class EndpointTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public EndpointTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_running_sagas_When_list_requested_Then_returns_filtered_items()
    {
        var runningId = await SeedSaga(OrderSagaStep.StockReserving, SagaStatus.Running);
        await SeedSaga(OrderSagaStep.Completed, SagaStatus.Completed);

        using var client = CreateServiceClient();

        var response = await client.GetAsync("/operator/api/sagas?type=Order&status=Running");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ListSagasResponse>();
        Assert.NotNull(body);
        Assert.Contains(body.Items, item => item.SagaId == runningId && item.Status == SagaStatus.Running.ToString());
        Assert.DoesNotContain(body.Items, item => item.Status == SagaStatus.Completed.ToString());
    }

    [Fact]
    public async Task Given_overdue_filter_When_list_requested_Then_returns_only_overdue_running_sagas()
    {
        var overdueId = await SeedSaga(
            OrderSagaStep.StockReserving,
            SagaStatus.Running,
            nextTimeoutAt: DateTime.UtcNow.AddMinutes(-1));
        await SeedSaga(OrderSagaStep.StockReserving, SagaStatus.Running);

        using var client = CreateServiceClient();

        var response = await client.GetAsync("/operator/api/sagas?overdue=true");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ListSagasResponse>();
        Assert.NotNull(body);
        Assert.Contains(body.Items, item => item.SagaId == overdueId);
        Assert.All(body.Items, item =>
        {
            Assert.NotNull(item.NextTimeoutAt);
            Assert.True(item.NextTimeoutAt <= DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task Given_no_credentials_When_list_requested_Then_returns_unauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/operator/api/sagas");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_openapi_requested_Then_operator_saga_paths_are_visible()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/operator/api/sagas", out _));
        Assert.True(paths.TryGetProperty("/operator/api/sagas/{id}", out _));
        Assert.True(paths.TryGetProperty("/operator/api/sagas/{id}/retry", out _));
        Assert.True(paths.TryGetProperty("/operator/api/sagas/{id}/abort", out _));
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
