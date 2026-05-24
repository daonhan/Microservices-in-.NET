using NetArchTest.Rules;

namespace Saga.Tests.Architecture;

public class LayoutTests
{
    private static readonly System.Reflection.Assembly SagaServiceAssembly = typeof(Program).Assembly;

    [Fact(Skip = "enabled in Phase 10")]
    public void Domain_DoesNotReference_InfrastructureFeaturesOrContracts()
    {
        var result = Types.InAssembly(SagaServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Saga.Service.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Saga.Service.Infrastructure",
                "Saga.Service.Features",
                "Saga.Service.Contracts")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Domain types may not reference Infrastructure, Features or Contracts: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = "enabled in Phase 10")]
    public void Features_DoNotReference_OtherFeatureSlices()
    {
        var featureTypes = Types.InAssembly(SagaServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Saga.Service.Features")
            .GetTypes()
            .ToList();

        var slices = featureTypes
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.StartsWith("Saga.Service.Features.", StringComparison.Ordinal))
            .Select(ns => ns.Split('.')[3])
            .Distinct()
            .ToList();

        var offenders = new List<string>();
        foreach (var slice in slices)
        {
            // Trailing '.' so the dependency match stops at a namespace boundary:
            // without it a shorter slice name would also match a longer one sharing its prefix.
            var otherSlices = slices.Where(s => !string.Equals(s, slice, StringComparison.Ordinal))
                .Select(s => $"Saga.Service.Features.{s}.")
                .ToArray();

            if (otherSlices.Length == 0)
            {
                continue;
            }

            // Anchored regex for the same boundary reason on the selector side.
            var result = Types.InAssembly(SagaServiceAssembly)
                .That()
                .ResideInNamespaceMatching($@"^Saga\.Service\.Features\.{slice}(\.|$)")
                .ShouldNot()
                .HaveDependencyOnAny(otherSlices)
                .GetResult();

            if (!result.IsSuccessful)
            {
                offenders.AddRange(result.FailingTypeNames ?? []);
            }
        }

        Assert.True(offenders.Count == 0,
            "Features.<X> may not reference Features.<Y> for X != Y: " + string.Join(", ", offenders));
    }

    [Fact(Skip = "enabled in Phase 10")]
    public void Infrastructure_DoesNotReference_Features()
    {
        var result = Types.InAssembly(SagaServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Saga.Service.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("Saga.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure types may not reference Saga.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = "enabled in Phase 10")]
    public void Contracts_DoNotReference_InternalLayers()
    {
        var result = Types.InAssembly(SagaServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Saga.Service.Contracts")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Saga.Service.Domain",
                "Saga.Service.Infrastructure",
                "Saga.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Contracts types may not reference Domain, Infrastructure or Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
