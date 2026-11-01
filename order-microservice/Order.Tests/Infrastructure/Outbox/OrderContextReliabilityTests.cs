using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.Outbox.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Order.Service.Features.CreateOrder;
using Order.Service.Infrastructure.Data.EntityFramework;
using Order.Service.Infrastructure.Outbox;
using Testcontainers.MsSql;

namespace Order.Tests.Infrastructure.Outbox;

[Trait("Category", "Integration")]
public sealed class OrderContextReliabilityTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private ServiceProvider _provider = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        _connectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString())
        { InitialCatalog = "OrderReliability" }.ConnectionString;
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:Default"] = _connectionString;
        var services = new ServiceCollection();
        services.AddOutbox(configuration);
        _provider = services.BuildServiceProvider();
        await using var context = new OrderContext(new DbContextOptionsBuilder<OrderContext>()
            .UseSqlServer(_connectionString).Options);
        await context.Database.MigrateAsync();
        using var scope = _provider.CreateScope();
        await ((DbContext)scope.ServiceProvider.GetRequiredService<IOutboxStore>()).Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _sql.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Given_rollback_before_or_during_commit_When_reusing_context_Then_events_and_pending_writes_survive(bool failDuringCommit)
    {
        using var scope = _provider.CreateScope();
        var store = new FaultingStore(scope.ServiceProvider.GetRequiredService<IOutboxStore>())
        { FailDuringCommit = failDuringCommit };
        await using var context = CreateContext(store);
        var order = CreateOrder();

        await Assert.ThrowsAnyAsync<Exception>(() => context.ExecuteAsync(() => context.CreateOrder(order)));
        Assert.Single(order.DomainEvents);
        Assert.Equal(EntityState.Added, context.Entry(order).State);
        await using (var verification = CreateContext(store))
        {
            Assert.False(await verification.Orders.AnyAsync(row => row.OrderId == order.OrderId));
        }
        Assert.Empty(await ReadOutboxIds());

        store.Fail = false;
        await context.ExecuteAsync(() => Task.CompletedTask);
        Assert.Empty(order.DomainEvents);
        Assert.Equal(EntityState.Unchanged, context.Entry(order).State);
        Assert.Single(await ReadOutboxIds());
        await using var committed = CreateContext(store);
        Assert.True(await committed.Orders.AnyAsync(row => row.OrderId == order.OrderId));
    }

    [Fact]
    public async Task Given_execution_strategy_retry_after_outbox_write_When_replayed_Then_command_runs_once_with_stable_event_id()
    {
        using var scope = _provider.CreateScope();
        var store = new FaultingStore(scope.ServiceProvider.GetRequiredService<IOutboxStore>()) { TransientFailure = true };
        await using var context = CreateContext(store, retry: true);
        var order = CreateOrder();
        var calls = 0;
        await context.ExecuteAsync(async () => { calls++; await context.CreateOrder(order); });

        Assert.Equal(1, calls);
        Assert.Equal(2, store.EventIds.Count);
        Assert.Equal(store.EventIds[0], store.EventIds[1]);
        Assert.Equal(store.EventIds[0], Assert.Single(await ReadOutboxIds()));
        Assert.Empty(order.DomainEvents);
        await using var committed = CreateContext(store);
        Assert.Equal(1, await committed.Orders.CountAsync(row => row.OrderId == order.OrderId));
    }

    [Fact]
    public async Task Given_outer_transaction_When_executing_Then_rejected_before_work_or_event_acknowledgement()
    {
        using var services = _provider.CreateScope();
        await using var context = CreateContext(services.ServiceProvider.GetRequiredService<IOutboxStore>());
        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var called = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.ExecuteAsync(() =>
        { called = true; return Task.CompletedTask; }));
        Assert.False(called);
    }

    private OrderContext CreateContext(IOutboxStore store, bool retry = false)
    {
        var options = new DbContextOptionsBuilder<OrderContext>().UseSqlServer(_connectionString, sql =>
        {
            if (retry)
            {
                sql.ExecutionStrategy(dependencies => new TestExecutionStrategy(dependencies));
            }
            else
            {
                sql.EnableRetryOnFailure();
            }
        }).Options;
        return new OrderContext(options, new DomainEventOutboxInterceptor([new OrderCreatedIntegrationMap()], store));
    }

    private async Task<List<Guid>> ReadOutboxIds()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [Id] FROM [OutboxEvents]";
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }
        return ids;
    }

    private static Service.Domain.Order CreateOrder()
    {
        var order = new Service.Domain.Order { CustomerId = "reliability-test" };
        order.AddOrderProduct("p-1", 2);
        order.Submit(new Dictionary<string, decimal> { ["p-1"] = 12.5m });
        return order;
    }

    private sealed class TestExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
    }

    private sealed class AbortOnPrepare : IEnlistmentNotification
    {
        public void Prepare(PreparingEnlistment preparingEnlistment) => preparingEnlistment.ForceRollback();
        public void Commit(Enlistment enlistment) => enlistment.Done();
        public void Rollback(Enlistment enlistment) => enlistment.Done();
        public void InDoubt(Enlistment enlistment) => enlistment.Done();
    }

    private sealed class FaultingStore(IOutboxStore inner) : IOutboxStore
    {
        public bool Fail { get; set; } = true;
        public bool FailDuringCommit { get; init; }
        public bool TransientFailure { get; init; }
        public List<Guid> EventIds { get; } = [];

        public async Task AddOutboxEvent<T>(T @event) where T : Event
        {
            EventIds.Add(@event.Id);
            await inner.AddOutboxEvent(@event);
            if (Fail)
            {
                Fail = false;
                if (FailDuringCommit)
                {
                    Transaction.Current!.EnlistVolatile(new AbortOnPrepare(), EnlistmentOptions.None);
                }
                else if (TransientFailure)
                {
                    throw new TimeoutException("simulated transient error after outbox insert");
                }
                else
                {
                    throw new InvalidOperationException("simulated failure after outbox insert");
                }
            }
        }
        public Task<List<OutboxEvent>> GetUnpublishedOutboxEvents() => inner.GetUnpublishedOutboxEvents();
        public Task MarkOutboxEventAsPublished(Guid id) => inner.MarkOutboxEventAsPublished(id);
        public Task<bool> RequeueOutboxEvent(Guid id) => inner.RequeueOutboxEvent(id);
        public Task RecordPublishFailure(Guid id, string error, int maxAttempts) => inner.RecordPublishFailure(id, error, maxAttempts);
        public Task<List<OutboxEvent>> GetFailedOutboxEvents() => inner.GetFailedOutboxEvents();
        public IExecutionStrategy CreateExecutionStrategy() => inner.CreateExecutionStrategy();
    }
}
