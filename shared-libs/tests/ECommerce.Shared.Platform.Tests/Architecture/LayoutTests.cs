using System.Reflection;
using NetArchTest.Rules;

namespace ECommerce.Shared.Platform.Tests.Architecture;

public class LayoutTests
{
    private static readonly Assembly PlatformAssembly =
        typeof(ECommerce.Shared.Authentication.AuthOptions).Assembly;

    [Fact]
    public void Platform_OwnsAuthObservabilityHealthChecksOpenApi()
    {
        var types = Types.InAssembly(PlatformAssembly)
            .That()
            .ResideInNamespaceStartingWith("ECommerce.Shared.Authentication")
            .Or()
            .ResideInNamespaceStartingWith("ECommerce.Shared.Observability")
            .Or()
            .ResideInNamespaceStartingWith("ECommerce.Shared.HealthChecks")
            .Or()
            .ResideInNamespaceStartingWith("ECommerce.Shared.OpenApi")
            .GetTypes()
            .ToList();

        Assert.NotEmpty(types);
    }
}
