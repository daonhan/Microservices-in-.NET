using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.ApiModels;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Models;
using Saga.Tests.Authentication;

namespace Saga.Tests.Api;

public class OperatorEndpointTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public OperatorEndpointTests(SagaWebApplicationFactory factory)
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
        var body = await response.Content.ReadFromJsonAsync<SagaListResponse>();
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
        var body = await response.Content.ReadFromJsonAsync<SagaListResponse>();
        Assert.NotNull(body);
        Assert.Contains(body.Items, item => item.SagaId == overdueId);
        Assert.All(body.Items, item =>
        {
            Assert.NotNull(item.NextTimeoutAt);
            Assert.True(item.NextTimeoutAt <= DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task Given_saga_with_transitions_When_detail_requested_Then_returns_history()
    {
        var sagaId = await SeedSaga(OrderSagaStep.PaymentAuthorizing, SagaStatus.Running);

        using var client = CreateServiceClient();

        var response = await client.GetAsync($"/operator/api/sagas/{sagaId}");

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<SagaDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(sagaId, detail.SagaId);
        Assert.NotNull(detail.Order);
        Assert.NotEmpty(detail.Transitions);
        Assert.Contains(detail.Transitions, transition =>
            transition.FromStep == OrderSagaStep.Started.ToString()
            && transition.ToStep == OrderSagaStep.PaymentAuthorizing.ToString());
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

    private async Task SeedFailedOutboxEvent(ReserveStockCommand command)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        await store.AddOutboxEvent(command);
        await store.RecordPublishFailure(command.Id, "operator retry test", maxAttempts: 1);
    }
}
