using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;

namespace Saga.Tests.Domain;

public class QaSeedFixturesTests : IClassFixture<SagaWebApplicationFactory>
{
    private static readonly Guid SeededSagaId = new("e0000000-0000-0000-0000-000000000001");
    private static readonly Guid SeededOrderId = new("e0000000-0000-0000-0000-000000000002");

    private readonly SagaWebApplicationFactory _factory;

    public QaSeedFixturesTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_FreshDatabase_When_MigrationsApplied_Then_OperatorSagaFixtureIsPresent()
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();

        var saga = await sagaContext.SagaInstances
            .AsNoTracking()
            .Include(s => s.OrderSagaState)
            .SingleAsync(s => s.SagaId == SeededSagaId);

        Assert.Equal("Order", saga.SagaType);
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Equal(OrderSagaStep.PaymentAuthorizing.ToString(), saga.CurrentStep);
        Assert.NotNull(saga.OrderSagaState);
        Assert.Equal(SeededOrderId, saga.OrderSagaState!.OrderId);
    }
}
