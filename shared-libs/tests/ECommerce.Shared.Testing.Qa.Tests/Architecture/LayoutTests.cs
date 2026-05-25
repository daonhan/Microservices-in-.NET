using System.Reflection;
using NetArchTest.Rules;

namespace ECommerce.Shared.Testing.Qa.Tests.Architecture;

public class LayoutTests
{
    private static readonly Assembly TestingQaAssembly =
        typeof(ECommerce.Shared.Qa.QaSeedingExtensions).Assembly;

    [Fact]
    public void TestingQa_DoesNotDepend_OnBrokersOrDeadLetter()
    {
        var result = Types.InAssembly(TestingQaAssembly)
            .That()
            .ResideInNamespaceStartingWith("ECommerce.Shared.Qa")
            .ShouldNot()
            .HaveDependencyOnAny(
                "ECommerce.Shared.Infrastructure.RabbitMq",
                "ECommerce.Shared.Infrastructure.AzureServiceBus",
                "ECommerce.Shared.Infrastructure.DeadLetter",
                "ECommerce.Shared.Authentication",
                "ECommerce.Shared.Observability")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Testing.Qa may not depend on brokers / DLQ / Platform: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
