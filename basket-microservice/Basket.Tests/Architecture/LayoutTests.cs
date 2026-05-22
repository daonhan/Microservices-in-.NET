using NetArchTest.Rules;

namespace Basket.Tests.Architecture;

public class LayoutTests
{
    private static readonly System.Reflection.Assembly BasketServiceAssembly = typeof(Program).Assembly;

    [Fact(Skip = "Enabled in phase 8")]
    public void Domain_DoesNotReference_InfrastructureOrFeatures()
    {
        var result = Types.InAssembly(BasketServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Basket.Service.Domain")
            .ShouldNot()
            .HaveDependencyOnAny("Basket.Service.Infrastructure", "Basket.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Domain types may not reference Basket.Service.Infrastructure or Basket.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = "Enabled in phase 8")]
    public void Features_DoNotReference_OtherFeatureSlices()
    {
        var featureTypes = Types.InAssembly(BasketServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Basket.Service.Features")
            .GetTypes()
            .ToList();

        var slices = featureTypes
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.StartsWith("Basket.Service.Features.", StringComparison.Ordinal))
            .Select(ns => ns.Split('.')[3])
            .Distinct()
            .ToList();

        var offenders = new List<string>();
        foreach (var slice in slices)
        {
            var otherSlices = slices.Where(s => !string.Equals(s, slice, StringComparison.Ordinal))
                .Select(s => $"Basket.Service.Features.{s}")
                .ToArray();

            if (otherSlices.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(BasketServiceAssembly)
                .That()
                .ResideInNamespaceStartingWith($"Basket.Service.Features.{slice}")
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

    [Fact(Skip = "Enabled in phase 8")]
    public void Infrastructure_DoesNotReference_Features()
    {
        var result = Types.InAssembly(BasketServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Basket.Service.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("Basket.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure types may not reference Basket.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = "Enabled in phase 8")]
    public void Contracts_DoNotReference_OtherBasketServiceNamespaces()
    {
        var result = Types.InAssembly(BasketServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Basket.Service.Contracts")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Basket.Service.Domain",
                "Basket.Service.Infrastructure",
                "Basket.Service.Features",
                "Basket.Service.Endpoints",
                "Basket.Service.ApiModels",
                "Basket.Service.IntegrationEvents")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Contracts types may not reference any other internal Basket.Service.* namespace: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
