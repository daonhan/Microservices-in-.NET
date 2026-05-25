using System.Reflection;
using NetArchTest.Rules;

namespace ECommerce.Shared.DeadLetter.Tests.Architecture;

public class LayoutTests
{
    private static readonly Assembly DeadLetterAssembly =
        typeof(ECommerce.Shared.Infrastructure.DeadLetter.IDeadLetterStore).Assembly;

    [Fact]
    public void Models_DoNotReference_Migrations()
    {
        var result = Types.InAssembly(DeadLetterAssembly)
            .That()
            .ResideInNamespaceStartingWith("ECommerce.Shared.Infrastructure.DeadLetter.Models")
            .ShouldNot()
            .HaveDependencyOn("ECommerce.Shared.Infrastructure.DeadLetter.Migrations")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "DeadLetter Models may not depend on Migrations: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
