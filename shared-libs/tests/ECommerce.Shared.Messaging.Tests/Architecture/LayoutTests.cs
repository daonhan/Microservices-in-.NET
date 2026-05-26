using System.Reflection;
using NetArchTest.Rules;

namespace ECommerce.Shared.Messaging.Tests.Architecture;

public class LayoutTests
{
    private static readonly Assembly MessagingAssembly =
        typeof(ECommerce.Shared.Infrastructure.Messaging.MessagingStartupExtensions).Assembly;

    [Fact]
    public void Messaging_DoesNotReference_DeadLetter()
    {
        var result = Types.InAssembly(MessagingAssembly)
            .ShouldNot()
            .HaveDependencyOn("ECommerce.Shared.Infrastructure.DeadLetter")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Messaging package may not depend on DeadLetter: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
