using System.Reflection;
using NetArchTest.Rules;

namespace ECommerce.Shared.AzureServiceBus.Tests.Architecture;

public class LayoutTests
{
    private static readonly Assembly AzureServiceBusAssembly =
        typeof(ECommerce.Shared.Infrastructure.AzureServiceBus.AzureServiceBusOptions).Assembly;

    [Fact]
    public void AzureServiceBus_Resides_In_OwnNamespace()
    {
        var types = Types.InAssembly(AzureServiceBusAssembly)
            .That()
            .ResideInNamespaceStartingWith("ECommerce.Shared.Infrastructure.AzureServiceBus")
            .GetTypes()
            .ToList();

        Assert.NotEmpty(types);
    }
}
