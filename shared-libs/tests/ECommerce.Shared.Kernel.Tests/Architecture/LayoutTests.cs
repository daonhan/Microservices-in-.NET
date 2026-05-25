using System.Reflection;
using NetArchTest.Rules;

namespace ECommerce.Shared.Kernel.Tests.Architecture;

public class LayoutTests
{
    private static readonly Assembly KernelAssembly =
        typeof(ECommerce.Shared.Infrastructure.Messaging.MessagingOptions).Assembly;

    [Fact]
    public void Abstractions_DoNotReference_Impl()
    {
        var result = Types.InAssembly(KernelAssembly)
            .That()
            .ResideInNamespaceStartingWith("ECommerce.Shared.Kernel.TelemetryConventions")
            .Or()
            .ResideInNamespaceStartingWith("ECommerce.Shared.Infrastructure.Messaging")
            .ShouldNot()
            .HaveDependencyOn("ECommerce.Shared.Observability.Metrics")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Kernel/Abstractions types may not depend on Kernel/Impl (Observability.Metrics): "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
