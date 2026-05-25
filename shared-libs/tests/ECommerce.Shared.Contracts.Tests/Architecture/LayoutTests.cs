using System.Reflection;
using NetArchTest.Rules;

namespace ECommerce.Shared.Contracts.Tests.Architecture;

public class LayoutTests
{
    private static readonly Assembly ContractsAssembly =
        typeof(ECommerce.Shared.IntegrationEvents.Commands.ReserveStockCommand).Assembly;

    [Fact]
    public void Contracts_OnlyDependOn_EventBusAbstractions()
    {
        var result = Types.InAssembly(ContractsAssembly)
            .That()
            .ResideInNamespaceStartingWith("ECommerce.Shared.IntegrationEvents")
            .ShouldNot()
            .HaveDependencyOnAny(
                "ECommerce.Shared.Authentication",
                "ECommerce.Shared.Observability",
                "ECommerce.Shared.HealthChecks",
                "ECommerce.Shared.OpenApi",
                "ECommerce.Shared.Infrastructure.RabbitMq",
                "ECommerce.Shared.Infrastructure.AzureServiceBus",
                "ECommerce.Shared.Infrastructure.DeadLetter",
                "ECommerce.Shared.Infrastructure.Outbox")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Contracts may not depend on Platform / broker / DLQ / Outbox: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
