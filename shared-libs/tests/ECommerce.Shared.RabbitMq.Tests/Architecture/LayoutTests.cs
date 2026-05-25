using System.Reflection;
using NetArchTest.Rules;

namespace ECommerce.Shared.RabbitMq.Tests.Architecture;

public class LayoutTests
{
    private static readonly Assembly RabbitMqAssembly =
        typeof(ECommerce.Shared.Infrastructure.RabbitMq.RabbitMqOptions).Assembly;

    [Fact]
    public void RabbitMq_TypesReside_InOwnNamespace()
    {
        var types = Types.InAssembly(RabbitMqAssembly)
            .That()
            .ResideInNamespaceStartingWith("ECommerce.Shared.Infrastructure.RabbitMq")
            .GetTypes()
            .ToList();

        Assert.NotEmpty(types);
    }
}
