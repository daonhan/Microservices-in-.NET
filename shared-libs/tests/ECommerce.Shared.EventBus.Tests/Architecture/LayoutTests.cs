using System.Reflection;
using NetArchTest.Rules;

namespace ECommerce.Shared.EventBus.Tests.Architecture;

public class LayoutTests
{
    private static readonly Assembly EventBusAssembly =
        typeof(ECommerce.Shared.Infrastructure.EventBus.Abstractions.IEventBus).Assembly;

    [Fact]
    public void Abstractions_DoNotReference_OutboxMigrations()
    {
        var result = Types.InAssembly(EventBusAssembly)
            .That()
            .ResideInNamespaceStartingWith("ECommerce.Shared.Infrastructure.EventBus.Abstractions")
            .ShouldNot()
            .HaveDependencyOn("ECommerce.Shared.Infrastructure.Outbox.Migrations")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "EventBus/Abstractions types may not depend on Outbox.Migrations: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
